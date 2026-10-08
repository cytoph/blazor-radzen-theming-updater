using NuGet.Versioning;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

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

    public static string GetRawContentAddress(string repositoryOwner, string repositoryName, string reference, string path)
    {
        string[] segments =
        [
            "https://raw.githubusercontent.com",
            repositoryOwner,
            repositoryName,
            reference,
            .. path.Split('/').Select(Uri.EscapeDataString)
        ];

        return string.Join('/', segments);
    }

    /// <summary>
    /// Computes the Git object ID of a blob with the specified content, i.e. the SHA-1 hash of the blob header followed by the content.
    /// </summary>
    /// <param name="content">The raw content of the blob.</param>
    /// <returns>The lowercase hexadecimal object ID, as reported by the GitHub API.</returns>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "Git object IDs are defined as SHA-1 hashes.")]
    public static string ComputeBlobSha(byte[] content)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        hash.AppendData(header);
        hash.AppendData(content);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
