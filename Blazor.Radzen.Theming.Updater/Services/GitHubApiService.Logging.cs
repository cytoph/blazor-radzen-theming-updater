using Microsoft.Extensions.Logging;

namespace Blazor.Radzen.Theming.Updater.Services;

partial class GitHubApiService
{
    [LoggerMessage(Level = LogLevel.Trace, Message = "Checking if tag {TagName} exists in repository.")]
    partial void LogCheckingTagExists(string tagName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Fetching base package repository contents recursively from path {FolderPath} at reference {Reference}.")]
    partial void LogFetchingRepositoryContents(string folderPath, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recursive listing of path {FolderPath} at reference {Reference} was truncated by GitHub; falling back to listing it directory by directory.")]
    partial void LogRepositoryTreeTruncated(string folderPath, string reference);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Fetching release URL for base package reference {Reference}.")]
    partial void LogFetchingBasePackageReleaseUrl(string reference);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Generating commit message for package version {PackageVersion} based on {BasePackageVersion}.")]
    partial void LogCommitMessageGenerated(string packageVersion, string basePackageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uploaded {UploadedFileCount} of {FileCount} files as new blobs; all others are already stored in the repository.")]
    partial void LogBlobsUploaded(int uploadedFileCount, int fileCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating commit on GitHub with {FileCount} files on branch \"{BranchName}\" with message \"{CommitMessage}\".")]
    partial void LogCreatingCommit(int fileCount, string branchName, string commitMessage);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating release on GitHub with tag \"{TagName}\" and name \"{ReleaseName}\".")]
    partial void LogCreatingRelease(string tagName, string releaseName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Will not delete branch {BranchName} as it is the scaffold branch of the repository!")]
    partial void LogWillNotDeleteScaffoldBranch(string branchName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting branch and associated releases for branch \"{BranchName}\".")]
    partial void LogDeletingBranchAndReleases(string branchName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting release \"{ReleaseName}\".")]
    partial void LogDeletingRelease(string releaseName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting tag \"{TagName}\".")]
    partial void LogDeletingTag(string tagName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting branch \"{BranchName}\".")]
    partial void LogDeletingBranch(string branchName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating new branch \"{BranchName}\" based on \"{BaseBranchName}\".")]
    partial void LogCreatingNewBranch(string branchName, string baseBranchName);
}
