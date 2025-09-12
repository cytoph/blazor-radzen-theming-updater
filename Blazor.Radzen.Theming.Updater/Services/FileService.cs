using Blazor.Radzen.Theming.Updater.Interfaces;
using Blazor.Radzen.Theming.Updater.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Frameworks;
using NuGet.Versioning;
using Octokit;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Blazor.Radzen.Theming.Updater.Services;

internal sealed partial class FileService : ICleanUpService
{
    private const string ResourcesFolderName = "Resources";
    private const string AssetsFolderName = "Assets";
    private const string TemplatesFolderName = "Templates";

    private const string OutputLibraryFolderName = "lib";

    private const string SpecificationTemplateFileName = "package.nuspec";
    private const string LicenseTemplateFileName = "LICENSE";
    private const string ReadmeTemplateFileName = "README.md";
    private const string BuildPropertiesTemplateFileName = "build.props";
    private const string EmptyLibraryFileName = "_._";

    private readonly ILogger<FileService> _logger;
    private readonly GeneralOptions _generalOptions;
    private readonly PackageManifest _packageManifest;
    private readonly BasePackageManifest _basePackageManifest;
    private readonly StagingOptions _stagingOptions;
    private readonly ArtifactOptions _artifactOptions;
    private readonly GitHubApiService _gitHubApiService;
    private readonly HttpClient _httpClient;

    private static readonly Assembly Assembly = typeof(FileService).Assembly;
    private static readonly string AssemblyName = Assembly.GetName().Name!;

    private string? _stagingFolder;

    public FileService(ILogger<FileService> logger,
        IOptions<GeneralOptions> generalOptions,
        IOptions<PackageManifest> packageManifest,
        IOptions<BasePackageManifest> basePackageManifest,
        IOptions<StagingOptions> stagingOptions,
        IOptions<ArtifactOptions> artifactOptions,
        GitHubApiService gitHubApiService,
        HttpClient httpClient)
    {
        _logger = logger;
        _generalOptions = generalOptions.Value;
        _packageManifest = packageManifest.Value;
        _basePackageManifest = basePackageManifest.Value;
        _stagingOptions = stagingOptions.Value;
        _artifactOptions = artifactOptions.Value;
        _gitHubApiService = gitHubApiService;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Creates a staging folder for the specified package version.
    /// </summary>
    /// <remarks>
    /// If the <see cref="_stagingOptions.CreateVersionSubfolders"/> option is enabled, the folder path will include a subdirectory named after the normalized
    /// package version. Otherwise, the base staging folder path is used. The method ensures that any existing folder at the target path is deleted before
    /// creating the new folder.
    /// </remarks>
    /// <param name="packageVersion">The version of the package for which the staging folder is created.</param>
    /// <returns>The relative path of the created staging folder.</returns>
    public string CreateStagingFolder(SemanticVersion packageVersion)
    {
        _stagingFolder = _stagingOptions.CreateVersionSubfolders
            ? Path.Combine(_stagingOptions.Folder, packageVersion.ToNormalizedString())
            : _stagingOptions.Folder;

        InternalDeleteStagingFolder(_stagingFolder);

        LogCreatingStagingFolder(_stagingFolder);
        Directory.CreateDirectory(_stagingFolder);

        return _stagingFolder;
    }

    /// <summary>
    /// Generates a license file in the staging folder using a predefined template and replacement tokens.
    /// </summary>
    /// <remarks>
    /// This method reads the license template file, replaces predefined tokens with values such as the authors and the applicable year(s), and writes the
    /// resulting content to a file in the staging folder.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    public async Task GenerateLicenseFile(CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string fileContent = await GetTemplateContent(LicenseTemplateFileName, cancellationToken);

        (int startYear, string authors) = (_generalOptions.StartYear, _generalOptions.Authors);
        string period = startYear == DateTime.Now.Year ? $"{startYear}" : $"{startYear}-{DateTime.Now.Year}";

        Dictionary<string, string> replacementTokens = new()
        {
            { "$period$", period },
            { "$authors$", authors },
        };

        fileContent = fileContent.ReplaceTokens(replacementTokens);

        string fileName = LicenseTemplateFileName;
        string filePath = Path.Combine(_stagingFolder, fileName);

        LogGeneratingLicenseFile(filePath);

        await File.WriteAllTextAsync(filePath, fileContent, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Generates a README file in the staging folder using a predefined template and token replacements.
    /// </summary>
    /// <remarks>
    /// This method reads the README template file, replaces predefined tokens with values such as the project content folder name, and writes the resulting
    /// content to a file in the staging folder.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    public async Task GenerateReadmeFile(CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string fileName = ReadmeTemplateFileName;
        string filePath = Path.Combine(_stagingFolder, fileName);

        LogGeneratingReadmeFile(filePath);

        string fileContent = await GetTemplateContent(ReadmeTemplateFileName, cancellationToken);

        Dictionary<string, string> replacementTokens = new()
        {
            { "$projectContentFolderName$", _artifactOptions.ProjectContentFolderName },
        };

        fileContent = fileContent.ReplaceTokens(replacementTokens);

        await File.WriteAllTextAsync(filePath, fileContent, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Generates a specification file in the staging folder using a predefined template and token replacements.
    /// </summary>
    /// <remarks>
    /// This method reads the specification template file, replaces predefined tokens with values such as the package ID and version, and writes the resulting
    /// content to a file in the staging folder.
    /// </remarks>
    /// <param name="packageVersion">
    /// The version of the package for which the specification file is being generated. This value is used to populate the version information in the file.
    /// </param>
    /// <param name="basePackageVersion">
    /// The version of the base package that the generated package depends on. This value is used to specify the dependency version in the file.
    /// </param>
    /// <param name="targetFrameworks">
    /// An array of target frameworks for which the package is compatible. Each framework is used to define a dependency group in the specification file.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.
    /// </param>
    /// <returns></returns>
    public async Task GenerateSpecificationFile(SemanticVersion packageVersion, SemanticVersion basePackageVersion, NuGetFramework[] targetFrameworks, CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string fileName = $"{_packageManifest.Id}{Path.GetExtension(SpecificationTemplateFileName)}";
        string filePath = Path.Combine(_stagingFolder, fileName);

        LogGeneratingSpecificationFile(filePath);

        string fileContent = await GetTemplateContent(SpecificationTemplateFileName, cancellationToken);

        Dictionary<string, string> replacementTokens = new()
        {
            { "$packageId$", _packageManifest.Id },
            { "$version$", packageVersion.ToNormalizedString() },
            { "$authors$", _generalOptions.Authors },
            { "$gitHubAddress$", _packageManifest.RepositoryAddress },
            { "$branch$", _packageManifest.RepositoryBranchName },
        };

        fileContent = fileContent.ReplaceTokens(replacementTokens);

        XDocument document = XDocument.Parse(fileContent);
        XNamespace ns = document.Root!.GetDefaultNamespace();
        XElement metadataElement = document.Element(ns + "package")!.Element(ns + "metadata")!;

        metadataElement.Add(new XElement(ns + "dependencies",
            targetFrameworks.Select(tfm => new XElement(ns + "group",
                new XAttribute("targetFramework", tfm.GetDependencyFrameworkName()),
                new XElement(ns + "dependency",
                    new XAttribute("id", _basePackageManifest.Id),
                    new XAttribute("version", basePackageVersion.ToNormalizedString()))))));

        fileContent = document.Declaration!.ToString() + Environment.NewLine + document.ToString();

        await File.WriteAllTextAsync(filePath, fileContent, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Generates library files for the specified target frameworks in the staging folder.
    /// </summary>
    /// <remarks>
    /// This method creates an empty library file for each specified target framework in the staging folder to avoid NU5128 warnings during packaging. If the
    /// specific target framework's folder does not exist (which it almost certainly won't), it will be created automatically.
    /// </remarks>
    /// <param name="targetFrameworks">
    /// An array of <see cref="NuGetFramework"/> objects representing the target frameworks for which library files should be generated.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.
    /// </param>
    public Task GenerateLibraryFiles(NuGetFramework[] targetFrameworks, CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string folderPath = Path.Combine(_stagingFolder, OutputLibraryFolderName);

        foreach (NuGetFramework targetFramework in targetFrameworks)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            string libraryFolderPath = Path.Combine(folderPath, targetFramework.GetShortFolderName());

            LogGeneratingLibraryFile(targetFramework, libraryFolderPath);

            Directory.CreateDirectory(libraryFolderPath);

            using (File.Create(Path.Combine(libraryFolderPath, EmptyLibraryFileName))) { } // create an empty file to avoid NU5128 warning
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a build properties file in the staging folder using a predefined template and token replacements.
    /// </summary>
    /// <remarks>
    /// This method reads the build properties template file, replaces predefined tokens with values such as the package and project content folder names,
    /// and writes the resulting content to a file in the staging folder. The file name is constructed using the package ID and the template file extension.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    public async Task GenerateBuildPropertiesFile(CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string folderPath = Path.Combine(_stagingFolder, _artifactOptions.PackageBuildFolderName);

        Directory.CreateDirectory(folderPath);

        string fileContent = await GetTemplateContent(BuildPropertiesTemplateFileName, cancellationToken);

        Dictionary<string, string> replacementTokens = new()
        {
            { "_PRFX_", _artifactOptions.BuildPropertyPrefix },
            { "$packageContentFolderName$", _artifactOptions.PackageContentFolderName },
            { "$projectContentFolderName$", _artifactOptions.ProjectContentFolderName },
        };

        fileContent = fileContent.ReplaceTokens(replacementTokens);

        string fileName = $"{_packageManifest.Id}{Path.GetExtension(BuildPropertiesTemplateFileName)}";
        string filePath = Path.Combine(folderPath, fileName);

        await File.WriteAllTextAsync(filePath, fileContent, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Copies content files from the specified repository reference to the content folder within the staging folder.
    /// </summary>
    /// <remarks>
    /// The name of the content folder within the staging folder is determined by the configuration.
    /// </remarks>
    /// <param name="repositoryReference">
    /// The reference to the repository from which content files will be pulled. This is typically a commit ID or tag reference.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.
    /// </param>
    /// <returns></returns>
    public async Task CopyContentFiles(string repositoryReference, CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string contentFolderPath = Path.Combine(_stagingFolder, _artifactOptions.PackageContentFolderName);

        LogPreparingContentFileFetch(repositoryReference, contentFolderPath);

        await InternalCopyContentFiles(contentFolderPath, string.Empty, repositoryReference, cancellationToken);
    }

    /// <summary>
    /// Copies asset files from the assembly resources to the staging folder.
    /// </summary>
    /// <remarks>
    /// The folder structure of the assets within the assembly resources is preserved in the staging folder.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    public async Task CopyAssetFiles(CancellationToken cancellationToken = default)
    {
        EnsureStagingFolder();

        string assetsResourcePath = $"{AssemblyName}.{ResourcesFolderName}.{AssetsFolderName}.";

        foreach (string assetResourcePath in Assembly.GetManifestResourceNames().Where(n => n.StartsWith(assetsResourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            string relativeResourcePath = assetResourcePath[assetsResourcePath.Length..];
            int lastDotIndex = relativeResourcePath.LastIndexOf('.');
            string relativeFilePath = $"{relativeResourcePath[..lastDotIndex].Replace('.', Path.DirectorySeparatorChar)}.{relativeResourcePath[(lastDotIndex + 1)..]}";
            string localFilePath = Path.Combine(_stagingFolder, relativeFilePath);
            string directoryPath = Path.GetDirectoryName(localFilePath)!;

            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            using Stream readStream = Assembly.GetManifestResourceStream(assetResourcePath)!;
            using Stream writeStream = File.Create(localFilePath);

            await readStream.CopyToAsync(writeStream, cancellationToken);
        }
    }

    /// <summary>
    /// Gets the content of a template file embedded as a resource in the assembly.
    /// </summary>
    /// <param name="templateFileName">The name of the template file whose content is to be retrieved.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns>The content of the specified template file as a string.</returns>
    private static async Task<string> GetTemplateContent(string templateFileName, CancellationToken cancellationToken = default)
    {
        using Stream? specificationStream = Assembly.GetManifestResourceStream($"{AssemblyName}.{ResourcesFolderName}.{TemplatesFolderName}.{templateFileName}");
        using StreamReader specificationReader = new(specificationStream!);

        return await specificationReader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the staging folder recursively, if it exists.
    /// </summary>
    public Task DeleteStagingFolderAsync()
    {
        InternalDeleteStagingFolder(_stagingOptions.Folder);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensures that the staging folder has been initialized.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the staging folder has not been initialized, indicating that the <see cref="CreateStagingFolder"/> method must be called first.
    /// </exception>
    [MemberNotNull(nameof(_stagingFolder))]
    private void EnsureStagingFolder()
    {
        if (_stagingFolder is null)
            throw new InvalidOperationException($"{nameof(FileService)} has not been initialized. Call {nameof(CreateStagingFolder)}() method first.");
    }

    /// <summary>
    /// Recursively copies content files from a specified repository path to a local content folder.
    /// </summary>
    /// <param name="contentFolderPath">The content folder base path where files will be copied to preserving relative directory structure.</param>
    /// <param name="relativePath">The relative path within the repository to start copying from.</param>
    /// <param name="repositoryReference">The reference to the repository (e.g., commit ID or tag) to pull files from.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    private async Task InternalCopyContentFiles(string contentFolderPath, string relativePath, string repositoryReference, CancellationToken cancellationToken = default)
    {
        string[] relativePathSegments = relativePath.Split('/');

        string localFolderPath = Path.Combine([contentFolderPath, .. relativePathSegments]);

        Directory.CreateDirectory(localFolderPath);

        string repositoryFolderPath = Path.Combine([_basePackageManifest.ContentFolderPath, .. relativePathSegments])
            .Replace(Path.DirectorySeparatorChar, '/'); // GitHub API expects forward slashes

        LogStartingRepositoryFolderEnumeration(repositoryFolderPath, localFolderPath);

        GitContent[] contents = await _gitHubApiService.GetBasePackageRepositoryContentsAsync(repositoryFolderPath, repositoryReference);

        LogRepositoryItemsFound(contents.Length, repositoryFolderPath);

        List<Task> tasks = [];

        foreach (GitContent content in contents)
        {
            if (content.Type == GitContentType.File)
            {
                string relativeFilePath = Path.GetRelativePath(_basePackageManifest.ContentFolderPath, content.Path);
                string localFilePath = Path.Combine(contentFolderPath, relativeFilePath);

                tasks.Add(DownloadFileAsync(content.DownloadUrl!, localFilePath, cancellationToken));
            }
            else if (content.Type == GitContentType.Directory)
            {
                string newRelativePath = Path.Combine(relativePath, content.Name).Replace(Path.DirectorySeparatorChar, '/');

                tasks.Add(InternalCopyContentFiles(contentFolderPath, newRelativePath, repositoryReference, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);

        async Task DownloadFileAsync(string downloadUrl, string localFilePath, CancellationToken cancellationToken = default)
        {
            LogDownloadingRepositoryFile(downloadUrl, localFilePath);

            using Stream readStream = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken);
            using FileStream writeStream = File.Create(localFilePath);

            await readStream.CopyToAsync(writeStream, cancellationToken);
        }
    }

    /// <summary>
    /// Deletes the staging folder recursively, if it exists.
    /// </summary>
    /// <param name="stagingFolder">The path of the staging folder to be deleted.</param>
    private void InternalDeleteStagingFolder(string stagingFolder)
    {
        if (!Directory.Exists(stagingFolder))
            return;

        LogDeletingStagingFolder(stagingFolder);

        Directory.Delete(stagingFolder, true);
    }

    /// <inheritdoc/>
    async Task<bool> ICleanUpService.CleanUpAsync(SemanticVersion latestPackageVersion, CancellationToken cancellationToken)
    {
        await DeleteStagingFolderAsync();
        return true;
    }
}
