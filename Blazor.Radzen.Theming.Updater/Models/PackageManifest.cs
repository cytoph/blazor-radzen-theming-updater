using Blazor.Radzen.Theming.Updater.Helpers;

namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Manifest describing the package being produced (Blazor.Radzen.Theming) and its repository details.
/// </summary>
internal class PackageManifest
{
    public const string SectionName = "Package";

    /// <summary>
    /// The NuGet package ID.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The owner of the package's GitHub repository.
    /// </summary>
    public required string RepositoryOwner { get; set; }

    /// <summary>
    /// The name of the package's GitHub repository.
    /// </summary>
    public required string RepositoryName { get; set; }

    /// <summary>
    /// The branch to use for commits and releases.
    /// </summary>
    public required string RepositoryBranchName { get; set; }

    /// <summary>
    /// Optional pre-release identifier for versioning. Gets suffixed with a dot and a Unix timestamp when used.
    /// </summary>
    public string? PreReleaseIdentifier { get; set; }

    /// <summary>
    /// The GitHub repository URL assembled from <see cref="RepositoryOwner"/> and <see cref="RepositoryName"/>.
    /// </summary>
    public string RepositoryAddress => GitHelpers.GetGitHubAddress(RepositoryOwner, RepositoryName);

    /// <summary>
    /// The branch reference used for Git operations.
    /// </summary>
    public string RepositoryBranchReference => GitHelpers.GetBranchReference(RepositoryBranchName);
}
