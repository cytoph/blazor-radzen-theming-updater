namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// General options controlling program metadata and run behavior.
/// </summary>
internal class GeneralOptions
{
    public const string SectionName = "General";

    /// <summary>
    /// Used in conjunction with the current year to determine the validity period for the license.
    /// </summary>
    public required int StartYear { get; set; }

    /// <summary>
    /// The authors of the package, used in license and metadata generation.
    /// </summary>
    public required string Authors { get; set; }

    /// <summary>
    /// Maximum number of versions to create in one execution. 0 means unlimited.
    /// </summary>
    public int VersionCreationLimit { get; set; }
}
