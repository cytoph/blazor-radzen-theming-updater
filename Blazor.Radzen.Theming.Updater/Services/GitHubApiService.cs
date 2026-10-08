using Blazor.Radzen.Theming.Updater.Helpers;
using Blazor.Radzen.Theming.Updater.Interfaces;
using Blazor.Radzen.Theming.Updater.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using Octokit;
using System.Diagnostics.CodeAnalysis;

namespace Blazor.Radzen.Theming.Updater.Services;

internal sealed partial class GitHubApiService : ICleanUpService
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    private readonly ILogger<GitHubApiService> _logger;
    private readonly GitHubOptions _options;
    private readonly PackageManifest _packageManifest;
    private readonly BasePackageManifest _basePackageManifest;
    private readonly GitHubClient _client;

    private (DateTimeOffset CreatedAt, IReadOnlyList<Release> Releases)? _basePackageReleasesCache;
    private (DateTimeOffset CreatedAt, IReadOnlyList<Reference> Tags)? _basePackageTagsCache;

    private HashSet<string>? _packageTagNames;
    private bool _targetBranchEnsured;
    private (string CommitSha, string TreeSha)? _targetBranchHead;
    private readonly HashSet<string> _knownBlobShas = new(StringComparer.OrdinalIgnoreCase);

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
    /// Checks if the specified Git tag exists in the package repository.
    /// </summary>
    /// <remarks>
    /// The names of all tags under the "refs/tags/" namespace are fetched from the repository once and then kept for the lifetime of the service; tags
    /// created or deleted by this service afterwards are applied to that list. The comparison is case-insensitive.
    /// </remarks>
    /// <param name="tagName">The name of the Git tag to check for (e.g. "v1.2.3").</param>
    /// <returns><see langword="true"/> if the Git tag exists; otherwise, <see langword="false"/>.</returns>
    [MemberNotNull(nameof(_packageTagNames))]
    public async Task<bool> PackageTagExists(string tagName)
    {
        LogCheckingTagExists(tagName);

        if (_packageTagNames is null)
        {
            // The compiler treats every await as a point where the method returns to its caller, so it cannot see that the
            // [MemberNotNull] postcondition is met once the returned task has completed - which is what all callers await.
#pragma warning disable CS8774
            IReadOnlyList<Reference> references = await _client.Git.Reference.GetAllForSubNamespace(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, GitHelpers.TagsCategory);
#pragma warning restore CS8774

            _packageTagNames = new(references.Select(r => GitHelpers.GetTagName(r.Ref)), StringComparer.OrdinalIgnoreCase);
        }

        return _packageTagNames.Contains(tagName);
    }

    /// <summary>
    /// Ensures that the specified branch exists in the repository. If the branch does not exist, it is created from the scaffold branch.
    /// </summary>
    /// <remarks>
    /// This method checks if the branch specified in the repository manifest exists. If the branch is not found, a new branch is created from the head of the
    /// scaffold branch, so every target branch starts from the bare repository scaffold (e.g. the CI workflow) instead of a previous revision's history.
    /// The scaffold branch itself must exist; the method fails otherwise rather than falling back to any other branch. The check is only performed once
    /// for the lifetime of the service.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scaffold branch does not exist in the repository.</exception>
    public async Task EnsureBranchExists()
    {
        if (_targetBranchEnsured)
        {
            return;
        }

        await InternalEnsureBranchExists();

        _targetBranchEnsured = true;
    }

    private async Task InternalEnsureBranchExists()
    {
        IReadOnlyList<Reference> branches = await _client.Git.Reference.GetAllForSubNamespace(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, GitHelpers.BranchesCategory);

        if (branches.Any(r => r.Ref == _packageManifest.TargetBranchReference)) // if branch already exists, do nothing
        {
            return;
        }

        Reference scaffoldBranch = branches.FirstOrDefault(r => r.Ref == _packageManifest.ScaffoldBranchReference)
            ?? throw new InvalidOperationException($"Scaffold branch \"{_packageManifest.ScaffoldBranchName}\" does not exist in the repository; it is required to create branch \"{_packageManifest.TargetBranchName}\".");

        LogCreatingNewBranch(_packageManifest.TargetBranchName, _packageManifest.ScaffoldBranchName);
        await _client.Git.Reference.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, new NewReference(_packageManifest.TargetBranchReference, scaffoldBranch.Object.Sha));
    }

    /// <summary>
    /// Retrieves the contents of a specified folder and all its subfolders in the base package repository at a given reference.
    /// </summary>
    /// <remarks>
    /// The whole folder is listed with a single recursive tree request. The download URLs point to the raw content host, which does not count against
    /// the GitHub API rate limit. If GitHub truncates the recursive listing, the folder is listed directory by directory instead, which costs one request
    /// per directory.
    /// </remarks>
    /// <param name="repositoryFolderPath">The path to the folder within the repository whose contents are to be retrieved.</param>
    /// <param name="reference">The Git reference (e.g., commit ID, or tag) to use when fetching the folder contents.</param>
    /// <returns>
    /// An array of <see cref="GitContent"/> objects representing the contents of the specified folder and its subfolders, with paths relative to the
    /// repository root.
    /// </returns>
    public async Task<GitContent[]> GetBasePackageRepositoryContentsAsync(string repositoryFolderPath, string reference)
    {
        LogFetchingRepositoryContents(repositoryFolderPath, reference);

        TreeResponse tree = await _client.Git.Tree.GetRecursive(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName, $"{reference}:{repositoryFolderPath}");

        if (tree.Truncated)
        {
            LogRepositoryTreeTruncated(repositoryFolderPath, reference);

            return await GetBasePackageRepositoryContentsByDirectoryAsync(repositoryFolderPath, reference);
        }

        return [.. tree.Tree.Select(t =>
        {
            string path = $"{repositoryFolderPath}/{t.Path}";

            GitContentType type = t.Type.Value switch
            {
                TreeType.Blob when t.Mode != Octokit.FileMode.Symlink => GitContentType.File,
                TreeType.Tree => GitContentType.Directory,
                _ => GitContentType.Other
            };

            return new GitContent()
            {
                Name = t.Path[(t.Path.LastIndexOf('/') + 1)..],
                Path = path,
                DownloadUrl = type == GitContentType.File
                    ? GitHelpers.GetRawContentAddress(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName, reference, path)
                    : null,
                Type = type,
            };
        })];
    }

    private async Task<GitContent[]> GetBasePackageRepositoryContentsByDirectoryAsync(string repositoryFolderPath, string reference)
    {
        IReadOnlyList<RepositoryContent> contents = await _client.Repository.Content.GetAllContentsByRef(_basePackageManifest.RepositoryOwner, _basePackageManifest.RepositoryName, repositoryFolderPath, reference);

        GitContent[] folderContents = [.. contents.Select(c => new GitContent()
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

        List<GitContent> allContents = [.. folderContents];

        foreach (GitContent directory in folderContents.Where(c => c.Type == GitContentType.Directory))
        {
            allContents.AddRange(await GetBasePackageRepositoryContentsByDirectoryAsync(directory.Path, reference));
        }

        return [.. allContents];
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
    /// <para>
    /// Only files whose content is not yet stored in the repository are uploaded as new blobs; all others reference the existing blob by its object ID,
    /// which is computed locally. The branch head is fetched once and then tracked from the commits created by this service. The branch reference is
    /// updated without force, so the update fails instead of overwriting commits that were pushed to the branch by anyone else in the meantime.
    /// </para>
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
        (string parentCommitSha, string currentTreeSha) = _targetBranchHead ??= await GetTargetBranchHead();

        TreeResponse currentTree = await _client.Git.Tree.GetRecursive(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, currentTreeSha);

        if (currentTree.Truncated) // the recursive listing is incomplete, so fall back to the root items and upload every file
        {
            currentTree = await _client.Git.Tree.Get(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, currentTreeSha);
        }
        else
        {
            _knownBlobShas.UnionWith(currentTree.Tree.Where(t => t.Type.Value == TreeType.Blob).Select(t => t.Sha));
        }

        NewTree newTree = new();

        string[] newRootItems = [.. Directory.EnumerateFileSystemEntries(stagingFolder).Select(p => Path.GetRelativePath(stagingFolder, p))];

        foreach (TreeItem item in currentTree.Tree.Where(t => !t.Path.Contains('/', StringComparison.Ordinal) && !newRootItems.Contains(t.Path, StringComparer.OrdinalIgnoreCase)))
        {
            newTree.Tree.Add(new()
            {
                Path = item.Path,
                Mode = item.Mode,
                Type = item.Type.Value,
                Sha = item.Sha,
            });
        }

        int fileCount = 0;
        int uploadedFileCount = 0;

        foreach (string filePath in Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(stagingFolder, filePath).Replace(Path.DirectorySeparatorChar, '/');
            byte[] rawContent = await File.ReadAllBytesAsync(filePath);
            string blobSha = GitHelpers.ComputeBlobSha(rawContent);

            fileCount++;

            if (!_knownBlobShas.Contains(blobSha))
            {
                NewBlob newBlob = new()
                {
                    Encoding = EncodingType.Base64,
                    Content = Convert.ToBase64String(rawContent),
                };

                BlobReference blob = await _client.Git.Blob.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, newBlob);

                if (!string.Equals(blob.Sha, blobSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Locally computed blob SHA {blobSha} of \"{relativePath}\" does not match the SHA {blob.Sha} returned by GitHub.");
                }

                _knownBlobShas.Add(blobSha);
                uploadedFileCount++;
            }

            newTree.Tree.Add(new()
            {
                Path = relativePath,
                Mode = Octokit.FileMode.File,
                Type = TreeType.Blob,
                Sha = blobSha,
            });
        }

        LogBlobsUploaded(uploadedFileCount, fileCount);
        LogCreatingCommit(newTree.Tree.Count, _packageManifest.TargetBranchName, commitMessage);

        TreeResponse tree = await _client.Git.Tree.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, newTree);

        NewCommit newCommit = new(commitMessage, tree.Sha, [parentCommitSha]);

        Commit commit = await _client.Git.Commit.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, newCommit);

        await _client.Git.Reference.Update(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, _packageManifest.TargetBranchReference, new ReferenceUpdate(commit.Sha));

        _targetBranchHead = (commit.Sha, tree.Sha);

        return commit.Sha;
    }

    /// <summary>
    /// Fetches the commit the target branch currently points to, along with that commit's tree.
    /// </summary>
    /// <returns>The SHA of the branch's head commit and the SHA of its tree.</returns>
    private async Task<(string CommitSha, string TreeSha)> GetTargetBranchHead()
    {
        Reference branchReference = await _client.Git.Reference.Get(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, _packageManifest.TargetBranchReference);
        Commit currentCommit = await _client.Git.Commit.Get(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, branchReference.Object.Sha);

        return (currentCommit.Sha, currentCommit.Tree.Sha);
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
    /// <para>
    /// The tag must not exist yet: GitHub would silently attach the release to the existing tag and ignore the target branch, so the release would point
    /// at the tag's old commit instead of the branch head.
    /// </para>
    /// </remarks>
    /// <param name="packageVersion">
    /// The version of the package to be released. Determines the tag name, release name, and whether the release is marked as a prerelease.
    /// </param>
    /// <param name="releaseNotes">
    /// The release notes describing the changes in this release. These will be included in the release body.
    /// </param>
    /// <exception cref="InvalidOperationException">The tag for <paramref name="packageVersion"/> already exists in the repository.</exception>
    public async Task CreateRelease(SemanticVersion packageVersion, string releaseNotes)
    {
        string tagName = GitHelpers.GenerateTagName(packageVersion);

        if (await PackageTagExists(tagName))
        {
            throw new InvalidOperationException($"Tag \"{tagName}\" already exists in the repository; a release for it would not point at the head of branch \"{_packageManifest.TargetBranchName}\".");
        }

        string releaseName = GitHelpers.GenerateReleaseName(packageVersion);

        LogCreatingRelease(tagName, releaseName);

        await _client.Repository.Release.Create(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, new NewRelease(tagName)
        {
            TargetCommitish = _packageManifest.TargetBranchReference,
            Name = releaseName,
            Body = releaseNotes,
            Prerelease = packageVersion.IsPrerelease,
        });

        _packageTagNames.Add(tagName); // creating the release has created the tag as well
    }

    /// <summary>
    /// Deletes the specified repository branch and all its associated tags and releases, if applicable.
    /// </summary>
    /// <remarks>
    /// This method deletes the branch specified in the package manifest, along with any tags and releases associated with commits in the branch. If the
    /// branch is the scaffold branch, the operation is not performed, and the method returns <see langword="false"/>.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> if the branch and its associated tags and releases were successfully deleted; otherwise, <see langword="false"/>
    /// if the operation was skipped due to the branch being the scaffold branch.
    /// </returns>
    public async Task<bool> DeleteBranchAndReleasesAsync()
    {
        if (_packageManifest.TargetBranchName == _packageManifest.ScaffoldBranchName)
        {
            LogWillNotDeleteScaffoldBranch(_packageManifest.TargetBranchName);
            return false;
        }

        LogDeletingBranchAndReleases(_packageManifest.TargetBranchName);

        IReadOnlyList<Reference> references = await _client.Git.Reference.GetAll(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName);
        IReadOnlyList<Reference> branches = [.. references.Where(r => GitHelpers.IsBranchReference(r.Ref))];
        IReadOnlyList<Reference> tags = [.. references.Where(r => GitHelpers.IsTagReference(r.Ref))];
        IReadOnlyList<Release> releases = await _client.Repository.Release.GetAll(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName);

        if (branches.FirstOrDefault(r => r.Ref == _packageManifest.TargetBranchReference) is { } otherBranch)
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

                _packageTagNames?.Remove(tagName);
            }

            string branchName = GitHelpers.GetBranchName(otherBranch.Ref);

            LogDeletingBranch(branchName);
            await _client.Git.Reference.Delete(_packageManifest.RepositoryOwner, _packageManifest.RepositoryName, otherBranch.Ref); // delete branch

            _targetBranchEnsured = false;
            _targetBranchHead = null;
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
