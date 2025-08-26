namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// GitHub integration options used for authentication and templated content.
/// </summary>
internal class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// The GitHub personal access token for authentication.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Template for commit messages.
    /// </summary>
    /// <remarks>
    /// Placeholders: {packageId}, {packageVersion}, {basePackageId}, {basePackageVersion}
    /// </remarks>
    public required string CommitMessageTemplate { get; set; }

    /// <summary>
    /// Template for release notes.
    /// </summary>
    /// <remarks>
    /// Placeholders: {packageId}, {packageVersion}, {basePackageId}, {basePackageVersion}, {commitMessage}, {basePackageReleaseUrl}
    /// </remarks>
    public required string ReleaseNoteTemplate { get; set; }
}
