using Blazor.Radzen.Theming.Updater.Helpers;
using Blazor.Radzen.Theming.Updater.Interfaces;
using Blazor.Radzen.Theming.Updater.Models;
using Blazor.Radzen.Theming.Updater.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Frameworks;
using NuGet.Versioning;
using System.Diagnostics;

namespace Blazor.Radzen.Theming.Updater.Commands;

internal sealed partial class CreateReleaseCommand : IDisposable
{
    private readonly ILogger<CreateReleaseCommand> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly GeneralOptions _generalOptions;
    private readonly StagingOptions _stagingOptions;
    private readonly PackageManifest _packageManifest;
    private readonly BasePackageManifest _basePackageManifest;
    private readonly IServiceScope _singletonScope;
    private readonly FileService _fileService;
    private readonly GitHubApiService _gitHubApiService;
    private readonly NuGetApiService _nuGetApiService;
    private readonly NuGetCliService _nuGetCliService;

    public CreateReleaseCommand(ILogger<CreateReleaseCommand> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<PackageManifest> packageManifest,
        IOptions<BasePackageManifest> basePackageManifest,
        IOptions<GeneralOptions> generalOptions,
        IOptions<StagingOptions> stagingOptions,
        GitHubApiService gitHubApiService,
        NuGetApiService nuGetApiService,
        NuGetCliService nuGetCliService)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _generalOptions = generalOptions.Value;
        _stagingOptions = stagingOptions.Value;
        _packageManifest = packageManifest.Value;
        _basePackageManifest = basePackageManifest.Value;
        _singletonScope = serviceScopeFactory.CreateScope();
        _fileService = _singletonScope.ServiceProvider.GetRequiredService<FileService>();
        _gitHubApiService = gitHubApiService;
        _nuGetApiService = nuGetApiService;
        _nuGetCliService = nuGetCliService;
    }

    /// <summary>
    /// Creates a release based on the base package version.
    /// </summary>
    /// <param name="noCommit">Do not create a commit and release on GitHub.</param>
    /// <param name="noRelease">Do not create a release on GitHub.</param>
    ///
    /// <param name="pack">Create a NuGet package.</param>
    /// <param name="push">Push the package to package source.</param>
    ///
    /// <param name="noCleanFiles">Do not clean up staging files before the operation.</param>
    /// <param name="cleanGithub">Clean up GitHub repository before the operation.</param>
    /// <param name="cleanNuget">Clean up latest NuGet package before the operation.</param>
    /// <param name="cleanAll">Clean up all staging files, GitHub repository, and latest NuGet package before the operation.</param>
    /// <returns></returns>
    [ConsoleAppFramework.Command("")]
    public async Task ExecuteAsync(
        bool noCommit = false,
        bool noRelease = false,

        bool pack = false,
        bool push = false,

        bool noCleanFiles = false,
        bool cleanGithub = false,
        bool cleanNuget = false,
        bool cleanAll = false,

        CancellationToken cancellationToken = default
    )
    {
        if (noCommit && push)
        {
            LogCannotUsePushWithNoCommit();
            return;
        }

        LogPackageInfo(_packageManifest.Id, _packageManifest.RepositoryOwner, _packageManifest.RepositoryName);
        LogBasePackageInfo(_basePackageManifest.Id, _basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName);

        SemanticVersion[] packageVersions = await _nuGetApiService.GetPackageVersions(_packageManifest.Id, cancellationToken);
        SemanticVersion latestPackageVersion = packageVersions.Max() ?? new(0, 0, 0);

        LogLatestPackageVersion(latestPackageVersion);

        SemanticVersion[] basePackageVersions = await _nuGetApiService.GetPackageVersions(_basePackageManifest.Id, cancellationToken);
        SemanticVersion[] higherBasePackageVersions = [.. basePackageVersions.Where(v => v > latestPackageVersion).Order()];

        if (higherBasePackageVersions.Length == 0)
        {
            LogNoHigherBasePackageVersion();
            return;
        }

        LogHigherBasePackageVersions(higherBasePackageVersions.Length);

        if (_generalOptions.VersionCreationLimit > 0 && higherBasePackageVersions.Length > _generalOptions.VersionCreationLimit)
        {
            higherBasePackageVersions = [.. higherBasePackageVersions.Take(_generalOptions.VersionCreationLimit)];

            LogLimitingVersionCreation(higherBasePackageVersions.Length);
        }

        if (cleanAll || !noCleanFiles || cleanGithub || cleanNuget)
        {
            List<(ICleanUpService Service, Action LogSuccess)> cleanUps = [];

            if (cleanAll || !noCleanFiles)
            {
                cleanUps.Add((_fileService, LogStagingFolderDeleted));
            }

            if (cleanAll || cleanGithub)
            {
                cleanUps.Add((_gitHubApiService, LogGitHubBranchAndReleasesDeleted));
            }

            if (cleanAll || cleanNuget)
            {
                cleanUps.Add((_nuGetCliService, LogNuGetPackageDeleted));
            }

            foreach ((ICleanUpService service, Action logSuccess) in cleanUps)
            {
                bool result = await service.CleanUpAsync(latestPackageVersion, cancellationToken);

                if (!result)
                {
                    LogCleanupOperationFailed();
                    return;
                }

                logSuccess();
            }
        }

        foreach (SemanticVersion basePackageVersion in higherBasePackageVersions)
        {
            LogPackageCreationStarted(basePackageVersion);

            long startTs = Stopwatch.GetTimestamp();

            using IServiceScope scope = _serviceScopeFactory.CreateScope();

            FileService iterationFileService = scope.ServiceProvider.GetRequiredService<FileService>();

            SemanticVersion packageVersion = !string.IsNullOrEmpty(_packageManifest.PreReleaseIdentifier)
                ? basePackageVersion.WithReleaseLabel($"{_packageManifest.PreReleaseIdentifier}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}")
                : new(basePackageVersion);

            LogPackageVersionResolved(packageVersion);

            string? existingTag = await _gitHubApiService.PackageTagExists(packageVersion);

            if (!string.IsNullOrEmpty(existingTag))
            {
                LogTagAlreadyExists(existingTag);
                return;
            }

            (string? baseCommitId, NuGetFramework[] targetFrameworks) = await _nuGetApiService.GetPackageSpecificationData(_basePackageManifest.Id, basePackageVersion, cancellationToken);

            LogTargetFrameworksExtracted(string.Join(", ", targetFrameworks.Select(tf => tf.GetShortFolderName())));

            string basePackageRepositoryReference = baseCommitId ?? GitHelpers.GetTagReference(basePackageVersion);

            string stagingFolder = iterationFileService.CreateStagingFolder(packageVersion);

            await iterationFileService.GenerateLicenseFile(cancellationToken);
            await iterationFileService.GenerateReadmeFile(cancellationToken);
            await iterationFileService.GenerateSpecificationFile(packageVersion, basePackageVersion, targetFrameworks, cancellationToken);
            await iterationFileService.GenerateLibraryFiles(targetFrameworks, cancellationToken);
            await iterationFileService.GenerateBuildPropertiesFile(cancellationToken);
            await iterationFileService.CopyContentFiles(basePackageRepositoryReference, cancellationToken);
            await iterationFileService.CopyAssetFiles(cancellationToken);

            LogPackageFilesAssembled(stagingFolder);

            string? commitId = null;

            if (!noCommit)
            {
                await _gitHubApiService.EnsureBranchExists();

                string? basePackageReleaseUrl = await _gitHubApiService.GetBasePackageReleaseUrl(basePackageRepositoryReference);
                string commitMessage = _gitHubApiService.GenerateCommitMessage(packageVersion, basePackageVersion);

                LogCommitMessageConstructed(commitMessage);

                commitId = await _gitHubApiService.CreateCommit(stagingFolder, commitMessage);

                LogFilesCommitted(_packageManifest.RepositoryBranchName, commitId[..7]);

                if (!noRelease)
                {
                    string releaseNotes = _gitHubApiService.GenerateReleaseNotes(packageVersion, basePackageVersion, commitMessage, basePackageReleaseUrl);

                    await _gitHubApiService.CreateRelease(packageVersion, releaseNotes);

                    LogReleaseCreated(packageVersion);
                }
                else
                {
                    LogReleaseSkipped();
                }
            }
            else
            {
                LogCommitSkipped();
            }

            if (pack || push)
            {
                commitId ??= "1234567"; // dummy commit ID for when no commit has been created

                string? packageFilePath = await _nuGetCliService.CreatePackageAsync(stagingFolder, packageVersion, commitId[7..], cancellationToken);

                if (packageFilePath == null)
                {
                    LogPackageCreationFailed();
                    return;
                }

                LogPackageFileCreated(packageFilePath);

                if (push)
                {
                    bool result = await _nuGetCliService.UploadPackageAsync(packageFilePath, cancellationToken);

                    if (!result)
                    {
                        LogPackageUploadFailed();
                        return;
                    }

                    LogPackageFileUploaded();
                }
            }

            TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTs);

            LogPackageCompleted(_packageManifest.Id, packageVersion, elapsedTime.Milliseconds);
        }

        if (!_stagingOptions.KeepFolder)
        {
            await _fileService.DeleteStagingFolderAsync();
        }
        else
        {
            LogStagingFolderDeletionSkipped();
        }
    }

    public void Dispose()
    {
        _nuGetApiService.Dispose();
        _singletonScope.Dispose();
    }
}
