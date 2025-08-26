using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Services;

partial class FileService
{
    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating staging folder at {Folder}.")]
    partial void LogCreatingStagingFolder(string folder);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Generating license file at {FilePath}.")]
    partial void LogGeneratingLicenseFile(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Generating README file at {FilePath}.")]
    partial void LogGeneratingReadmeFile(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Generating specification file at {FilePath}.")]
    partial void LogGeneratingSpecificationFile(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Creating library file for target framework {TargetFramework} at {FolderPath}.")]
    partial void LogGeneratingLibraryFile(NuGetFramework targetFramework, string folderPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Generating build properties file at {FilePath}.")]
    partial void LogGeneratingBuildPropertiesFile(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Preparing to fetch content files from repository reference {RepositoryReference} into staging folder {FolderPath}.")]
    partial void LogPreparingContentFileFetch(string repositoryReference, string folderPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Retrieving asset content from file {FilePath}.")]
    partial void LogRetrievingAssetContent(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Copying asset file to {FilePath}.")]
    partial void LogCopyingAssetFile(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Retrieving template content from file {FilePath}.")]
    partial void LogRetrievingTemplateContent(string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Starting enumeration of repository folder {RepositoryFolderPath} into staging folder {StagingFolderPath}.")]
    partial void LogStartingRepositoryFolderEnumeration(string repositoryFolderPath, string stagingFolderPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Found {ContentCount} repository items in folder {FolderPath}.")]
    partial void LogRepositoryItemsFound(int contentCount, string folderPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Downloading repository file from {DownloadUrl} to {FilePath}.")]
    partial void LogDownloadingRepositoryFile(string downloadUrl, string filePath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Deleting staging folder {StagingFolderPath}.")]
    partial void LogDeletingStagingFolder(string stagingFolderPath);
}
