using Blazor.Radzen.Theming.Updater.Helpers;
using Blazor.Radzen.Theming.Updater.Models;
using Blazor.Radzen.Theming.Updater.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Frameworks;
using NuGet.Versioning;
using System.Diagnostics;

namespace Blazor.Radzen.Theming.Updater.Commands;

internal sealed partial class RepublishCommand : IDisposable
{
    private readonly ILogger<RepublishCommand> _logger;
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

    public RepublishCommand(ILogger<RepublishCommand> logger,
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
    /// Republishes existing package versions using the current updater pipeline.
    /// </summary>
    /// <remarks>
    /// Iterates over all Radzen versions that have a corresponding theming package version,
    /// builds each with the current pipeline (including <see cref="VersionHelper.UpdaterVersion"/>),
    /// and publishes the result. Versions whose padded tag already exists are skipped, making
    /// the command idempotent.
    /// </remarks>
    /// <param name="from">Only republish Radzen versions at or above this version (e.g. "5.0.0").</param>
    /// <param name="noCommit">Do not create a commit and release on GitHub.</param>
    /// <param name="noRelease">Do not create a release on GitHub.</param>
    /// <param name="pack">Create a NuGet package.</param>
    /// <param name="push">Push the package to the package source.</param>
    [ConsoleAppFramework.Command("republish")]
    public async Task ExecuteAsync(
        string? from = null,

        bool noCommit = false,
        bool noRelease = false,

        bool pack = false,
        bool push = false,

        CancellationToken cancellationToken = default)
    {
        if (noCommit && push)
        {
            LogCannotUsePushWithNoCommit();
            return;
        }

        LogRepublishStarted(VersionHelper.UpdaterVersion);

        SemanticVersion? fromVersion = null;

        if (!string.IsNullOrEmpty(from))
        {
            if (!SemanticVersion.TryParse(from, out fromVersion))
            {
                LogInvalidFromVersion(from);
                return;
            }

            LogFilteringFromVersion(fromVersion);
        }

        SemanticVersion[] packageVersions = await _nuGetApiService.GetPackageVersions(_packageManifest.Id, cancellationToken);

        if (packageVersions.Length == 0)
        {
            LogNoExistingVersions();
            return;
        }

        LogExistingVersionsFound(packageVersions.Length);

        SemanticVersion[] basePackageVersions = await _nuGetApiService.GetPackageVersions(_basePackageManifest.Id, cancellationToken);

        SemanticVersion[] coveredBaseVersions = [.. packageVersions
            .Select(v => VersionHelper.IsOldFormat(v) ? v : VersionHelper.ToBaseVersion(v))
            .Distinct()
            .Where(v => basePackageVersions.Contains(v))
            .Order()];

        if (fromVersion is not null)
        {
            coveredBaseVersions = [.. coveredBaseVersions.Where(v => v >= fromVersion)];
        }

        if (coveredBaseVersions.Length == 0)
        {
            LogNoVersionsToRepublish();
            return;
        }

        LogVersionsToRepublish(coveredBaseVersions.Length);

        if (_generalOptions.VersionCreationLimit > 0 && coveredBaseVersions.Length > _generalOptions.VersionCreationLimit)
        {
            coveredBaseVersions = [.. coveredBaseVersions.Take(_generalOptions.VersionCreationLimit)];

            LogLimitingVersionCreation(coveredBaseVersions.Length);
        }

        foreach (SemanticVersion basePackageVersion in coveredBaseVersions)
        {
            LogPackageCreationStarted(basePackageVersion);

            long startTs = Stopwatch.GetTimestamp();

            SemanticVersion paddedBaseVersion = VersionHelper.ToPackageVersion(basePackageVersion);

            SemanticVersion packageVersion = !string.IsNullOrEmpty(_packageManifest.PreReleaseIdentifier)
                ? paddedBaseVersion.WithReleaseLabel($"{_packageManifest.PreReleaseIdentifier}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}")
                : paddedBaseVersion;

            LogPackageVersionResolved(packageVersion);

            string? existingTag = await _gitHubApiService.PackageTagExists(packageVersion);

            if (!string.IsNullOrEmpty(existingTag))
            {
                LogTagAlreadyExists(existingTag);
                continue;
            }

            using IServiceScope scope = _serviceScopeFactory.CreateScope();

            FileService iterationFileService = scope.ServiceProvider.GetRequiredService<FileService>();

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

                LogFilesCommitted(_packageManifest.TargetBranchName, commitId[..7]);

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
                commitId ??= new string('0', 40); // git's null SHA as dummy for when no commit has been created

                string? packageFilePath = await _nuGetCliService.CreatePackageAsync(stagingFolder, packageVersion, commitId, cancellationToken);

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

        LogRepublishCompleted();
    }

    public void Dispose()
    {
        _nuGetApiService.Dispose();
        _singletonScope.Dispose();
    }
}
