using Blazor.Radzen.Theming.Updater.Helpers;

namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Manifest describing the base package (Radzen.Blazor) and its repository details.
/// </summary>
internal class BasePackageManifest
{
    public const string SectionName = "BasePackage";

    /// <summary>
    /// The NuGet package ID of the base package.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The owner of the base package's GitHub repository.
    /// </summary>
    public required string RepositoryOwner { get; set; }

    /// <summary>
    /// The name of the base package's GitHub repository.
    /// </summary>
    public required string RepositoryName { get; set; }

    /// <summary>
    /// The folder path in the base package repository containing content files.
    /// </summary>
    public required string ContentFolderPath { get; set; }

    /// <summary>
    /// The GitHub repository URL assembled from <see cref="RepositoryOwner"/> and <see cref="RepositoryName"/>.
    /// </summary>
    public string RepositoryAddress => GitHelpers.GetGitHubAddress(RepositoryOwner, RepositoryName);
}
