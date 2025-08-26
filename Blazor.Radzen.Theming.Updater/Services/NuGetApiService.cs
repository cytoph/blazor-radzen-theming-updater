using Blazor.Radzen.Theming.Updater.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Services;

internal sealed partial class NuGetApiService : IDisposable
{
    private readonly ILogger<NuGetApiService> _logger;
    private readonly SourceRepository _repository;

    private readonly NuGet.Common.ILogger _nugetLogger;
    private readonly SourceCacheContext _sourceCacheContext;

    public NuGetApiService(ILogger<NuGetApiService> logger,
        IOptions<NuGetOptions> apiOptions)
    {
        _logger = logger;
        _repository = Repository.Factory.GetCoreV3(apiOptions.Value.Source);

        _nugetLogger = NullLogger.Instance;
        _sourceCacheContext = new() { NoCache = true };
    }

    /// <summary>
    /// Retrieves all available versions of the NuGet package with the specified <paramref name="packageId"/>.
    /// </summary>
    /// <param name="packageId">The unique identifier of the NuGet package to retrieve versions for.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns>
    /// An array of <see cref="SemanticVersion"/> objects representing the available versions of the specified package.
    /// The array will be empty if no versions are found.
    /// </returns>
    public async Task<SemanticVersion[]> GetPackageVersions(string packageId, CancellationToken cancellationToken = default)
    {
        LogFetchingPackageVersions(packageId);

        PackageMetadataResource resource = await _repository.GetResourceAsync<PackageMetadataResource>();

        IEnumerable<IPackageSearchMetadata> metadata = await resource.GetMetadataAsync(packageId, true, false, _sourceCacheContext, _nugetLogger, cancellationToken);

        IEnumerable<NuGetVersion> versions = metadata.Select(m => m.Identity.Version);

        return [.. versions];
    }

    /// <summary>
    /// Retrieves the commit ID and target frameworks specified in the NuGet package metadata.
    /// </summary>
    /// <remarks>
    /// This method fetches the NuGet package from the configured repository, reads its metadata, and extracts the repository commit ID (if available) and the
    /// list of target frameworks defined in the package.
    /// </remarks>
    /// <param name="packageId">The ID of the NuGet package to retrieve metadata for.</param>
    /// <param name="packageVersion">The version of the NuGet package to retrieve metadata for.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns>
    /// A tuple containing the commit ID and an array of target frameworks specified in the package metadata. The commit ID may be <see langword="null"/> if it
    /// is not specified in the package metadata. The array of target frameworks will be empty if no target frameworks are defined.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if the package cannot be downloaded or its metadata cannot be retrieved.</exception>
    public async Task<(string? CommitId, NuGetFramework[] TargetFrameworks)> GetPackageSpecificationData(string packageId, SemanticVersion packageVersion, CancellationToken cancellationToken = default)
    {
        LogFetchingTargetFrameworks(packageId, packageVersion);

        FindPackageByIdResource resource = await _repository.GetResourceAsync<FindPackageByIdResource>();

        using MemoryStream stream = new();

        bool result = await resource.CopyNupkgToStreamAsync(packageId, packageVersion.ToNuGetVersion(), stream, _sourceCacheContext, _nugetLogger, cancellationToken);

        if (!result)
        {
            throw new InvalidOperationException($"Failed to download package {packageId} version {packageVersion}.");
        }

        using PackageArchiveReader packageReader = new(stream);

        NuspecReader nuspecReader = await packageReader.GetNuspecReaderAsync(cancellationToken);

        string? commitId = nuspecReader.GetRepositoryMetadata().Commit.NullIfEmpty();
        IEnumerable<NuGetFramework> targetFrameworks = nuspecReader.GetDependencyGroups().Select(dg => dg.TargetFramework);

        return (commitId, [.. targetFrameworks]);
    }

    #region IDisposable

    public void Dispose()
    {
        _sourceCacheContext.Dispose();
    }

    #endregion // IDisposable
}
