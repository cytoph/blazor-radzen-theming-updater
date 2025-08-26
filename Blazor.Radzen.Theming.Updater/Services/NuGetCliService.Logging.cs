using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Services;

partial class NuGetCliService
{
    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating NuGet package from staging folder {StagingFolderPath} for package version {PackageVersion} in folder {PackageFolderPath}.")]
    partial void LogCreatingPackage(string stagingFolderPath, SemanticVersion packageVersion, string packageFolderPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create NuGet package. Error: {ErrorMessage}")]
    partial void LogCreatingPackageFailed(string errorMessage);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Uploading package file {PackageFilePath} to package source.")]
    partial void LogUploadingPackage(string packageFilePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to upload package file to package source. Error: {ErrorMessage}")]
    partial void LogUploadingPackageFailed(string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Will not delete non-prerelease package versions!")]
    private partial void LogWillNotDeleteNonPrereleasePackageVersions();

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting package version {PackageVersion} from package source.")]
    partial void LogDeletingPackage(SemanticVersion packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete package version from package source. Error: {ErrorMessage}")]
    partial void LogDeletingPackageFailed(string errorMessage);
}
