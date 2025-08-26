namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Represents an entry returned from the GitHub contents API.
/// </summary>
internal class GitContent
{
    /// <summary>
    /// The entry's name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Repository path to the entry.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Direct download URL, when the entry is a file.
    /// </summary>
    public required string? DownloadUrl { get; set; }

    /// <summary>
    /// The entry's type.
    /// </summary>
    public required GitContentType Type { get; set; }
}

/// <summary>
/// Types of content entries in a Git repository.
/// </summary>
internal enum GitContentType
{
    Other,
    File,
    Directory,
}
