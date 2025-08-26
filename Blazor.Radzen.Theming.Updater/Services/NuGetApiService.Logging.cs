using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Services;

partial class NuGetApiService
{
    [LoggerMessage(Level = LogLevel.Trace, Message = "Fetching available package versions for {PackageId} from package source.")]
    partial void LogFetchingPackageVersions(string packageId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Fetching supported target frameworks for {PackageId} version {PackageVersion}.")]
    partial void LogFetchingTargetFrameworks(string packageId, SemanticVersion packageVersion);
}
