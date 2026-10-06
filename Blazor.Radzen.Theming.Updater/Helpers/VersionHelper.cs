using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Helpers;

/// <summary>
/// Converts between base package (Radzen) versions and padded theming package versions.
/// </summary>
/// <remarks>
/// <para>
/// The theming package uses a padded versioning scheme: the base package's patch number is multiplied
/// by <see cref="PaddingFactor"/>, and the current <see cref="UpdaterVersion"/> is added. For example,
/// with UpdaterVersion 1: Radzen 9.2.4 becomes theming 9.2.401.
/// </para>
/// <para>
/// The last two digits encode which version of the updater pipeline produced the package. Bump
/// <see cref="UpdaterVersion"/> whenever the packaging logic changes (e.g. a fix to the SCSS
/// post-processing) and republish to propagate the change to all versions.
/// </para>
/// </remarks>
internal static class VersionHelper
{
    public const int PaddingFactor = 100;

    /// <summary>
    /// The current updater pipeline version. Encoded in the last two digits of the patch number.
    /// Increment when the packaging logic changes and a republish is needed.
    /// </summary>
    public const int UpdaterVersion = 1;

    /// <summary>
    /// Converts a base package version to the corresponding padded theming package version,
    /// incorporating the current <see cref="UpdaterVersion"/>.
    /// </summary>
    /// <param name="baseVersion">The base package (Radzen) version.</param>
    /// <returns>The padded theming package version (e.g. 9.2.4 → 9.2.401 with UpdaterVersion 1).</returns>
    public static SemanticVersion ToPackageVersion(SemanticVersion baseVersion)
        => new(baseVersion.Major, baseVersion.Minor, baseVersion.Patch * PaddingFactor + UpdaterVersion);

    /// <summary>
    /// Converts a padded theming package version back to the base package version it tracks.
    /// The updater version (last two digits) is stripped by integer division.
    /// </summary>
    /// <param name="packageVersion">The padded theming package version.</param>
    /// <returns>The base package (Radzen) version (e.g. 9.2.401 → 9.2.4).</returns>
    public static SemanticVersion ToBaseVersion(SemanticVersion packageVersion)
        => new(packageVersion.Major, packageVersion.Minor, packageVersion.Patch / PaddingFactor);

    /// <summary>
    /// Returns whether a version uses the old (unpadded) format, i.e. the patch number
    /// is below <see cref="PaddingFactor"/> and therefore IS the Radzen patch directly.
    /// </summary>
    public static bool IsOldFormat(SemanticVersion version)
        => version.Patch < PaddingFactor;
}
