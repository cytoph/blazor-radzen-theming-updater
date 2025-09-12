using NuGet.Versioning;

namespace Blazor.Radzen.Theming.Updater.Helpers;

internal static class GitHelpers
{
    public const string ReleasesPath = "releases";

    public const string RootNamespace = "refs";
    public const string BranchesCategory = "heads";
    public const string BranchesNamespace = $"{RootNamespace}/{BranchesCategory}";
    public const string TagsCategory = "tags";
    public const string TagsNamespace = $"{RootNamespace}/{TagsCategory}";

    public static string GetGitHubAddress(string repositoryOwner, string repositoryName)
        => $"https://github.com/{repositoryOwner}/{repositoryName}";

    public static string GetBranchReferenceShortName(string branchName) => $"{BranchesCategory}/{branchName}";

    public static string GetBranchReference(string branchName) => $"{BranchesNamespace}/{branchName}";

    public static bool IsBranchReference(string branchReference)
    {
        return branchReference.StartsWith($"{BranchesNamespace}/", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetBranchName(string branchReference)
    {
        if (!IsBranchReference(branchReference))
        {
            throw new ArgumentException($"Invalid branch reference: {branchReference}. It should start with '{BranchesNamespace}/'.");
        }

        return branchReference.Replace($"{BranchesNamespace}/", string.Empty);
    }

    public static string GenerateTagName(SemanticVersion version) => $"v{version}";

    public static string GetTagReferenceShortName(SemanticVersion version) => $"{TagsCategory}/{GenerateTagName(version)}";

    public static string GetTagReference(SemanticVersion version) => $"{TagsNamespace}/{GenerateTagName(version)}";

    public static bool IsTagReference(string tagReference)
    {
        return tagReference.StartsWith($"{TagsNamespace}/", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetTagName(string tagReference)
    {
        if (!IsTagReference(tagReference))
        {
            throw new ArgumentException($"Invalid tag reference: {tagReference}. It should start with '{TagsNamespace}/'.");
        }

        return tagReference.Replace($"{TagsNamespace}/", string.Empty);
    }

    public static string GenerateReleaseName(SemanticVersion version) => version.ToNormalizedString();
}
