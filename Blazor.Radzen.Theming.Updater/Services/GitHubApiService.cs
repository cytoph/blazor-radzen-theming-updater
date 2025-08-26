using Blazor.Radzen.Theming.Updater.Helpers;
using Blazor.Radzen.Theming.Updater.Interfaces;
using Blazor.Radzen.Theming.Updater.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using Octokit;

namespace Blazor.Radzen.Theming.Updater.Services;

internal partial class GitHubApiService : ICleanUpService
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);
    private static readonly string[] MainBranchNames = ["main", "master"];

    private readonly ILogger<GitHubApiService> _logger;
    private readonly GitHubOptions _options;
    private readonly PackageManifest _packageManifest;
    private readonly BasePackageManifest _basePackageManifest;
    private readonly GitHubClient _client;

    private (DateTimeOffset CreatedAt, IReadOnlyList<Release> Releases)? _basePackageReleasesCache;
    private (DateTimeOffset CreatedAt, IReadOnlyList<Reference> Tags)? _basePackageTagsCache;

    public GitHubApiService(ILogger<GitHubApiService> logger,
        IHostEnvironment environment,
        IOptions<GitHubOptions> options,
        IOptions<PackageManifest> packageManifest,
        IOptions<BasePackageManifest> basePackageManifest)
    {
        _logger = logger;
        _options = options.Value;
        _packageManifest = packageManifest.Value;
        _basePackageManifest = basePackageManifest.Value;

        _client = new(new ProductHeaderValue(environment.ApplicationName))
        {
            Credentials = new(options.Value.Token),
        };
    }

    /// <summary>
    /// Checks if a Git tag corresponding to the specified package version exists in the package repository.
    /// </summary>
    /// <remarks>
    /// This method queries the repository for all tags under the "refs/tags/" namespace and checks for a match with the tag corresponding to the specified
    /// package version. The comparison is case-insensitive.
    /// </remarks>
    /// <param name="packageVersion">The semantic version of the package to check for a corresponding Git tag.</param>
    /// <returns>The name of the Git tag if it exists; otherwise, <see langword="null"/>.</returns>
    public async Task<string?> PackageTagExists(SemanticVersion packageVersion)
    {
        string tagReference = GitHelpers.GetTagReference(packageVersion);

        LogCheckingTagExists(tagReference);

        IReadOnlyList<Reference> references = await _client.Git.Reference.GetAllForSubNamespace(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, GitHelpers.TagsCategory);

        return references.Select(r => r.Ref)
            .Where(r => string.Equals(r, tagReference, StringComparison.OrdinalIgnoreCase))
            .Select(GitHelpers.GetTagName)
            .FirstOrDefault();
    }

    /// <summary>
    /// Ensures that the specified branch exists in the repository. If the branch does not exist, it is created based on the main branch.
    /// </summary>
    /// <remarks>
    /// This method checks if the branch specified in the repository manifest exists. If the branch is not found and the repository contains a recognized main
    /// branch, a new branch is created from the main branch. The operation assumes that main branches (e.g., "main", "master") always exist.
    /// </remarks>
    public async Task EnsureBranchExists()
    {
        if (MainBranchNames.Contains(_packageManifest.RepositoryBranchName)) // we just assume main branches exist
        {
            return;
        }

        IReadOnlyList<Reference> branches = await _client.Git.Reference.GetAllForSubNamespace(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, GitHelpers.BranchesCategory);

        if (branches.Any(r => r.Ref == _packageManifest.RepositoryBranchReference)) // if branch already exists, do nothing
        {
            return;
        }

        Reference mainBranch = branches.First(r => MainBranchNames.Any(b => r.Ref == GitHelpers.GetBranchReference(b)));
        string mainBranchName = GitHelpers.GetBranchName(mainBranch.Ref);

        LogCreatingNewBranch(_packageManifest.RepositoryBranchName, mainBranchName);
        await _client.Git.Reference.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, new NewReference(_packageManifest.RepositoryBranchReference, mainBranch.Object.Sha));
    }

    /// <summary>
    /// Retrieves the contents of a specified folder in the base package repository at a given reference.
    /// </summary>
    /// <param name="repositoryFolderPath">The path to the folder within the repository whose contents are to be retrieved.</param>
    /// <param name="reference">The Git reference (e.g., commit ID, or tag) to use when fetching the folder contents.</param>
    /// <returns>An array of <see cref="GitContent"/> objects representing the contents of the specified folder.</returns>
    public async Task<GitContent[]> GetBasePackageRepositoryContentsAsync(string repositoryFolderPath, string reference)
    {
        LogFetchingRepositoryContents(repositoryFolderPath, reference);

        IReadOnlyList<RepositoryContent> contents = await _client.Repository.Content.GetAllContentsByRef(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName, repositoryFolderPath, reference);

        return [..contents.Select(c => new GitContent()
        {
            Name = c.Name,
            Path = c.Path,
            DownloadUrl = c.DownloadUrl,
            Type = c.Type.Value switch
            {
                ContentType.File => GitContentType.File,
                ContentType.Dir => GitContentType.Directory,
                _ => GitContentType.Other
            }
        })];
    }

    /// <summary>
    /// Retrieves the URL of the base package release associated with the specified Git reference.
    /// </summary>
    /// <remarks>
    /// This method resolves the provided Git reference to a tag name, if necessary, and searches for
    /// a release associated with that tag. If the reference does not correspond to a valid tag or release, the method
    /// returns <see langword="null"/>.
    /// </remarks>
    /// <param name="reference">The Git reference, which can be a commit ID or a tag reference.</param>
    /// <returns>The URL of the base package release if a matching release is found; otherwise, <see langword="null"/>.</returns>
    public async Task<string?> GetBasePackageReleaseUrl(string reference)
    {
        LogFetchingBasePackageReleaseUrl(reference);

        IReadOnlyList<Release> releases = await GetCachedBasePackageReleases();

        string? tagName = GitHelpers.IsTagReference(reference) ? GitHelpers.GetTagName(reference) : null;

        if (string.IsNullOrEmpty(tagName))
        {
            IReadOnlyList<Reference> tags = await GetCachedBasePackageTags();

            Reference? tag = tags.FirstOrDefault(t => t.Object.Type == TaggedType.Commit && t.Object.Sha == reference);

            if (tag is null)
            {
                return null;
            }

            tagName = GitHelpers.GetTagName(tag.Ref);
        }

        return releases.FirstOrDefault(r => r.TagName == tagName)?.HtmlUrl;
    }

    /// <summary>
    /// Generates a commit message by replacing tokens in the commit message template with values derived from the specified package versions.
    /// </summary>
    /// <remarks>
    /// The commit message template is defined in the options and may include tokens such as <c>{packageId}</c>, <c>{packageVersion}</c>,
    /// <c>{basePackageId}</c>, and <c>{basePackageVersion}</c>. These tokens are replaced with the appropriate values from the package manifest and the
    /// provided parameters.
    /// </remarks>
    /// <param name="packageVersion">
    /// The version of the current package. This value is used to populate the <c>{packageVersion}</c> token in the commit message template.
    /// </param>
    /// <param name="basePackageVersion">
    /// The version of the base package. This value is used to populate the <c>{basePackageVersion}</c> token in the commit message template.
    /// </param>
    /// <returns>A string representing the generated commit message with all tokens replaced by their corresponding values.</returns>
    public string GenerateCommitMessage(SemanticVersion packageVersion, SemanticVersion basePackageVersion)
    {
        Dictionary<string, string> replacementTokens = new()
        {
            { "{packageId}", _packageManifest.Id },
            { "{packageVersion}", packageVersion.ToNormalizedString() },
            { "{basePackageId}", _basePackageManifest.Id },
            { "{basePackageVersion}", basePackageVersion.ToNormalizedString() },
        };

        return _options.CommitMessageTemplate.ReplaceTokens(replacementTokens);
    }

    /// <summary>
    /// Creates a new commit in the specified repository branch using the files from the given staging folder and pushes it to the remote repository.
    /// </summary>
    /// <remarks>
    /// This method stages all files in the specified <paramref name="stagingFolder"/> and creates a commit in the repository branch defined by the current
    /// package manifest. After creation, the branch reference is updated to point to the new commit's ID (aka. pushing the commit).
    /// </remarks>
    /// <param name="stagingFolder">
    /// The path to the folder containing the files to be included in the commit. All files in this folder and its subdirectories will be added to the commit.
    /// </param>
    /// <param name="commitMessage">
    /// The message describing the changes in the commit.
    /// </param>
    /// <returns>The SHA identifier of the newly created commit.</returns>
    public async Task<string> CreateCommit(string stagingFolder, string commitMessage)
    {
        NewTree newTree = new();

        foreach (string filePath in Directory.GetFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(stagingFolder, filePath).Replace(Path.DirectorySeparatorChar, '/');
            string content = await File.ReadAllTextAsync(filePath);

            newTree.Tree.Add(new()
            {
                Path = relativePath,
                Content = content,
                Mode = Octokit.FileMode.File,
            });
        }

        LogCreatingCommit(newTree.Tree.Count, _packageManifest.RepositoryBranchName, commitMessage);

        TreeResponse tree = await _client.Git.Tree.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, newTree);

        Reference branchReference = await _client.Git.Reference.Get(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, _packageManifest.RepositoryBranchReference);

        NewCommit newCommit = new(commitMessage, tree.Sha, [branchReference.Object.Sha]);

        Commit commit = await _client.Git.Commit.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, newCommit);

        await _client.Git.Reference.Update(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, _packageManifest.RepositoryBranchReference, new ReferenceUpdate(commit.Sha));

        return commit.Sha;
    }

    /// <summary>
    /// Generates release notes for the current package version based on the provided information.
    /// </summary>
    /// <remarks>
    /// The release note template is defined in the options and may include tokens such as <c>{packageId}</c>, <c>{packageVersion}</c>, <c>{basePackageId}</c>,
    /// <c>{basePackageVersion}</c>, <c>{commitMessage}</c>, and <c>{basePackageReleaseUrl}</c>. These tokens are replaced with the appropriate values from the
    /// package manifest and the provided parameters.
    /// </remarks>
    /// <param name="packageVersion">
    /// The version of the current package. This value is used to populate the <c>{packageVersion}</c> token in the release note template.
    /// </param>
    /// <param name="basePackageVersion">
    /// The version of the base package. This value is used to populate the <c>{basePackageVersion}</c> token in the release note template.
    /// </param>
    /// <param name="commitMessage">
    /// The message of the commit associated with this release. This value is used to populate the <c>{commitMessage}</c> token in the release note template.
    /// </param>
    /// <param name="basePackageReleaseUrl">
    /// The URL of the base package's release notes. If <see langword="null"/>, a default URL is constructed using the base package's repository address.
    /// </param>
    /// <returns>A string representing the generated release notes with all tokens replaced by their corresponding values.</returns>
    public string GenerateReleaseNotes(SemanticVersion packageVersion, SemanticVersion basePackageVersion, string commitMessage, string? basePackageReleaseUrl)
    {
        Dictionary<string, string> replacementTokens = new()
        {
            { "{packageId}", _packageManifest.Id },
            { "{packageVersion}", packageVersion.ToNormalizedString() },
            { "{basePackageId}", _basePackageManifest.Id },
            { "{basePackageVersion}", basePackageVersion.ToNormalizedString() },
            { "{commitMessage}", commitMessage },
            { "{basePackageReleaseUrl}", basePackageReleaseUrl ?? $"{_basePackageManifest.RepositoryAddress}/{GitHelpers.ReleasesPath}" },
        };

        return _options.ReleaseNoteTemplate.ReplaceTokens(replacementTokens);
    }

    /// <summary>
    /// Creates a new release in the repository with the specified version and release notes.
    /// </summary>
    /// <remarks>
    /// This method generates a tag name and release name based on the provided <paramref name="packageVersion"/> and creates a release in the repository
    /// specified by the package manifest. The release is associated with the branch reference defined in the package manifest.
    /// </remarks>
    /// <param name="packageVersion">
    /// The version of the package to be released. Determines the tag name, release name, and whether the release is marked as a prerelease.
    /// </param>
    /// <param name="releaseNotes">
    /// The release notes describing the changes in this release. These will be included in the release body.
    /// </param>
    public async Task CreateRelease(SemanticVersion packageVersion, string releaseNotes)
    {
        string tagName = GitHelpers.GenerateTagName(packageVersion);
        string releaseName = GitHelpers.GenerateReleaseName(packageVersion);

        LogCreatingRelease(tagName, releaseName);

        await _client.Repository.Release.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, new NewRelease(tagName)
        {
            TargetCommitish = _packageManifest.RepositoryBranchReference,
            Name = releaseName,
            Body = releaseNotes,
            Prerelease = packageVersion.IsPrerelease,
        });
    }

    /// <summary>
    /// Deletes the specified repository branch and all its associated tags and releases, if applicable.
    /// </summary>
    /// <remarks>
    /// This method deletes the branch specified in the package manifest, along with any tags and releases associated with commits in the branch. If the
    /// branch is one of the main branches ("main" or "master"), the operation is not performed, and the method returns <see langword="false"/>.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> if the branch and its associated tags and releases were successfully deleted; otherwise, <see langword="false"/>
    /// if the operation was skipped due to the branch being a main branch.
    /// </returns>
    public async Task<bool> DeleteBranchAndReleasesAsync()
    {
        if (MainBranchNames.Contains(_packageManifest.RepositoryBranchName))
        {
            LogWillNotDeleteMainBranch(_packageManifest.RepositoryBranchName);
            return false;
        }

        LogDeletingBranchAndReleases(_packageManifest.RepositoryBranchName);

        IReadOnlyList<Reference> references = await _client.Git.Reference.GetAll(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName);
        IReadOnlyList<Reference> branches = [.. references.Where(r => GitHelpers.IsBranchReference(r.Ref))];
        IReadOnlyList<Reference> tags = [.. references.Where(r => GitHelpers.IsTagReference(r.Ref))];
        IReadOnlyList<Release> releases = await _client.Repository.Release.GetAll(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName);

        if (branches.FirstOrDefault(r => r.Ref == _packageManifest.RepositoryBranchReference) is { } otherBranch)
        {
            CommitRequest commitRequest = new() { Sha = otherBranch.Object.Sha };
            IReadOnlyList<GitHubCommit> commits = await _client.Repository.Commit.GetAll(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, commitRequest);

            foreach (Reference tag in tags.Where(r => r.Object.Type == TaggedType.Commit))
            {
                if (!commits.Any(c => c.Sha == tag.Object.Sha))
                    continue;

                string tagName = GitHelpers.GetTagName(tag.Ref);

                if (releases.FirstOrDefault(r => r.TagName == tagName) is { } release)
                {
                    LogDeletingRelease(release.Name);
                    await _client.Repository.Release.Delete(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, release.Id);
                }

                LogDeletingTag(tagName);
                await _client.Git.Reference.Delete(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, tag.Ref); // delete tag
            }

            string branchName = GitHelpers.GetBranchName(otherBranch.Ref);

            LogDeletingBranch(branchName);
            await _client.Git.Reference.Delete(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, otherBranch.Ref); // delete branch
        }

        return true;
    }

    /// <summary>
    /// Retrieves a cached list of base package releases, refreshing the cache if it has expired.
    /// </summary>
    /// <remarks>
    /// The cache is refreshed if it is older than the configured expiration time. If the cache is refreshed, the method fetches the latest releases from the
    /// repository specified in the base package manifest.
    /// </remarks>
    /// <returns>A read-only list of <see cref="Release"/> objects representing the base package releases.</returns>
    private async Task<IReadOnlyList<Release>> GetCachedBasePackageReleases()
    {
        DateTimeOffset maxAge = DateTimeOffset.UtcNow.Add(-CacheExpiration);

        if (_basePackageReleasesCache is not { CreatedAt: DateTimeOffset createdAt, Releases: IReadOnlyList<Release> releases } || createdAt <= maxAge)
        {
            releases = await _client.Repository.Release.GetAll(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName);

            _basePackageReleasesCache = (DateTimeOffset.UtcNow, releases);
        }

        return releases;
    }

    /// <summary>
    /// Retrieves a cached list of base package tags, refreshing the cache if it has expired.
    /// </summary>
    /// <remarks>
    /// The cache is refreshed if it is older than the configured expiration time. If the cache is refreshed, the method fetches the latest tags from the
    /// repository specified in the base package manifest.
    /// </remarks>
    /// <returns>A read-only list of <see cref="Reference"/> objects representing the base package tags.</returns>
    private async Task<IReadOnlyList<Reference>> GetCachedBasePackageTags()
    {
        DateTimeOffset maxAge = DateTimeOffset.UtcNow.Add(-CacheExpiration);

        if (_basePackageTagsCache is not { CreatedAt: DateTimeOffset createdAt, Tags: IReadOnlyList<Reference> tags } || createdAt <= maxAge)
        {
            tags = await _client.Git.Reference.GetAllForSubNamespace(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName, GitHelpers.TagsCategory);
            _basePackageTagsCache = (DateTimeOffset.UtcNow, tags);
        }

        return tags;
    }

    /// <inheritdoc/>
    Task<bool> ICleanUpService.CleanUpAsync(SemanticVersion latestPackageVersion, CancellationToken cancellationToken)
        => DeleteBranchAndReleasesAsync();
}
