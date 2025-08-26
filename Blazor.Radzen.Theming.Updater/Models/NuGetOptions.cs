namespace Blazor.Radzen.Theming.Updater.Models;

/// <summary>
/// NuGet publishing options.
/// </summary>
internal class NuGetOptions
{
    public const string SectionName = "NuGet";

    /// <summary>
    /// The NuGet source URL.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// The API key for publishing to NuGet.
    /// </summary>
    public required string ApiKey { get; set; }
}
