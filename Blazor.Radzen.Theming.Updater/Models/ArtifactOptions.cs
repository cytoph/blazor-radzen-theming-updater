namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Artifact packaging options used when generating the NuGet package contents and build assets.
/// </summary>
internal class ArtifactOptions
{
    public const string SectionName = "Artifact";

    /// <summary>
    /// The folder name for MSBuild files (like .props/.targets).
    /// </summary>
    public required string PackageBuildFolderName { get; set; }

    /// <summary>
    /// The folder name for content files. Files from the base package repository's <c>ContentFolderPath</c> are put here.
    /// </summary>
    public required string PackageContentFolderName { get; set; }

    /// <summary>
    /// Prefix for build properties in MSBuild files.
    /// </summary>
    public required string BuildPropertyPrefix { get; set; }

    /// <summary>
    /// The folder in the <c>obj</c> directory of the referencing project where content files are copied during restore.
    /// </summary>
    public required string ProjectContentFolderName { get; set; }
}
