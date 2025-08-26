using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Interfaces;

internal interface ICleanUpService
{
    /// <summary>
    /// Asynchronously performs cleanup operations based on the specified latest package version.
    /// </summary>
    /// <remarks>
    /// This method is designed to remove outdated or unnecessary resources based on the provided package version between iterations of version creation.
    /// </remarks>
    /// <param name="latestPackageVersion">The latest version of the package to use as a reference for cleanup operations.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns><see langword="true"/> if the cleanup was successful; otherwise, <see langword="false"/>.</returns>
    public Task<bool> CleanUpAsync(SemanticVersion latestPackageVersion, CancellationToken cancellationToken = default);
}
