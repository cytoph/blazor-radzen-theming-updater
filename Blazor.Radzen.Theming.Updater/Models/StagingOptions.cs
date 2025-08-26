namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// Staging options controlling how temporary folder(s) are created and retained during execution.
/// </summary>
internal class StagingOptions
{
    public const string SectionName = "Staging";

    /// <summary>
    /// The root folder for staging files.
    /// </summary>
    public string Folder { get; set; } = "output";

    /// <summary>
    /// Whether to keep the staging folder after execution.
    /// </summary>
    public bool KeepFolder { get; set; }

    /// <summary>
    /// Whether to create subfolders for each version in the staging folder.
    /// </summary>
    public bool CreateVersionSubfolders { get; set; }
}
