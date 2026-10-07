using Blazor.Radzen.Theming.Updater.Helpers;

namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Manifest describing the package being produced (Blazor.Radzen.Theming) and its repository details.
/// </summary>
internal sealed class PackageManifest
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
    /// The branch containing only the repository scaffold (e.g. the CI workflow). Missing target
    /// branches are created from its head.
    /// </summary>
    public required string ScaffoldBranchName { get; set; }

    /// <summary>
    /// The branch receiving the commits and releases. Defaults to the current updater revision's
    /// branch (e.g. "rev-0" for <see cref="VersionHelper.UpdaterVersion"/> 0), so bumping the
    /// updater version automatically targets a fresh branch.
    /// </summary>
    public string TargetBranchName { get; set; } = $"rev-{VersionHelper.UpdaterVersion}";

    /// <summary>
    /// Optional pre-release identifier for versioning. Gets suffixed with a dot and a Unix timestamp when used.
    /// </summary>
    public string? PreReleaseIdentifier { get; set; }

    /// <summary>
    /// The GitHub repository URL assembled from <see cref="RepositoryOwner"/> and <see cref="RepositoryName"/>.
    /// </summary>
    public string RepositoryAddress => GitHelpers.GetGitHubAddress(RepositoryOwner, RepositoryName);

    /// <summary>
    /// The scaffold branch reference used for Git operations.
    /// </summary>
    public string ScaffoldBranchReference => GitHelpers.GetBranchReference(ScaffoldBranchName);

    /// <summary>
    /// The target branch reference used for Git operations.
    /// </summary>
    public string TargetBranchReference => GitHelpers.GetBranchReference(TargetBranchName);
}
