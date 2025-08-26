using NuGet.Frameworks;
using NuGet.Versioning;
using System.Text.RegularExpressions;

namespace Blazor.Radzen.Theming.Updater;

internal static partial class Extensions
{
    public static string GetDependencyFrameworkName(this NuGetFramework framework)
    {
        if (string.Equals(framework.Framework, FrameworkConstants.FrameworkIdentifiers.Net, StringComparison.OrdinalIgnoreCase))
        {
            return $"{framework.Framework}{framework.Version.Major}.{framework.Version.Minor}";
        }
        else if (string.Equals(framework.Framework, FrameworkConstants.FrameworkIdentifiers.NetStandard, StringComparison.OrdinalIgnoreCase))
        {
            return $"{framework.Framework}{framework.Version.Major}.{framework.Version.Minor}";
        }
        else
        {
            return framework.GetShortFolderName();
        }
    }

    public static NuGetVersion ToNuGetVersion(this SemanticVersion version)
        => new(version.Major, version.Minor, version.Patch, 0, version.ReleaseLabels, version.Metadata);

    public static SemanticVersion WithReleaseLabel(this SemanticVersion packageVersion, string releaseLabel)
    {
        string newReleaseLabel = string.Join('.', new[] { packageVersion.Release, releaseLabel }.Where(s => !string.IsNullOrEmpty(s)));

        return new SemanticVersion(packageVersion.Major, packageVersion.Minor, packageVersion.Patch, newReleaseLabel);
    }

    public static string ReplaceTokens(this string input, Dictionary<string, string> replacementTokens)
    {
        string pattern = string.Join("|", replacementTokens.Keys.Select(Regex.Escape));

        return Regex.Replace(input, pattern, m => replacementTokens[m.Value]);
    }

    public static string? NullIfEmpty(this string? input)
        => string.IsNullOrEmpty(input) ? null : input;
}
