using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Commands;

partial class RepublishCommand
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting republish with updater version {UpdaterVersion}.")]
    partial void LogRepublishStarted(int updaterVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid --from version: \"{From}\". Expected a semantic version (e.g. 5.0.0).")]
    partial void LogInvalidFromVersion(string from);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filtering to base package versions >= {FromVersion}.")]
    partial void LogFilteringFromVersion(SemanticVersion fromVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "No existing package versions found. Nothing to republish.")]
    partial void LogNoExistingVersions();

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Count} existing package versions found.")]
    partial void LogExistingVersionsFound(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "No versions match the republish criteria.")]
    partial void LogNoVersionsToRepublish();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} base package versions to republish.")]
    partial void LogVersionsToRepublish(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Limiting creation of new versions to {VersionCount}.")]
    partial void LogLimitingVersionCreation(int versionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting package creation for base package version {BasePackageVersion}.")]
    partial void LogPackageCreationStarted(SemanticVersion basePackageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved package version will be {PackageVersion}.")]
    partial void LogPackageVersionResolved(SemanticVersion packageVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag {TagName} already exists in the repository. Aborting further operations.")]
    partial void LogTagAlreadyExists(string tagName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Target frameworks extracted from base package are {TargetFrameworks}.")]
    partial void LogTargetFrameworksExtracted(string targetFrameworks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "All package files have been assembled in {StagingFolder}.")]
    partial void LogPackageFilesAssembled(string stagingFolder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Constructed commit message is \"{CommitMessage}\".")]
    partial void LogCommitMessageConstructed(string commitMessage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Files have been committed and pushed to branch {BranchName}. Commit ID is {CommitId}.")]
    partial void LogFilesCommitted(string branchName, string commitId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Release has been created for version {Version}.")]
    partial void LogReleaseCreated(SemanticVersion version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Release creation has been skipped.")]
    partial void LogReleaseSkipped();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Commit has been skipped.")]
    partial void LogCommitSkipped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create package. Aborting further operations.")]
    partial void LogPackageCreationFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Package file has been created at {PackageFilePath}.")]
    partial void LogPackageFileCreated(string packageFilePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to upload package file to package source. Aborting further operations.")]
    partial void LogPackageUploadFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Package has been uploaded to package source successfully.")]
    partial void LogPackageFileUploaded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot use --push option when --no-commit is set. Please commit first to enable pushing.")]
    partial void LogCannotUsePushWithNoCommit();

    [LoggerMessage(Level = LogLevel.Information, Message = "Package {PackageId} version {Version} has been completed in {ElapsedTimeMS} ms.")]
    partial void LogPackageCompleted(string packageId, SemanticVersion version, int elapsedTimeMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Staging folder deletion has been skipped.")]
    partial void LogStagingFolderDeletionSkipped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Republish completed.")]
    partial void LogRepublishCompleted();
}
