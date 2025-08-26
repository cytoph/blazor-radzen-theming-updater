using Blazor.Radzen.Theming.Updater.Interfaces;
using Blazor.Radzen.Theming.Updater.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using System.Diagnostics;

namespace Blazor.Radzen.Theming.Updater.Services;

internal partial class NuGetCliService : ICleanUpService
{
    private readonly ILogger<NuGetCliService> _logger;
    private readonly PackageManifest _packageManifest;
    private readonly NuGetOptions _nuGetOptions;

    public NuGetCliService(ILogger<NuGetCliService> logger,
        IOptions<PackageManifest> packageManifest,
        IOptions<NuGetOptions> nuGetOptions)
    {
        _logger = logger;
        _packageManifest = packageManifest.Value;
        _nuGetOptions = nuGetOptions.Value;
    }

    /// <summary>
    /// Creates a NuGet package using the specified staging folder, package version, and commit ID.
    /// </summary>
    /// <remarks>
    /// This method uses the NuGet CLI to create the package. Ensure that the NuGet CLI is installed and available in the system's PATH. The method logs the
    /// package creation process and any errors encountered. If the NuGet CLI process exits with a non-zero code, the method returns <see langword="null"/>.
    /// </remarks>
    /// <param name="stagingFolder">
    /// The directory where the package creation process will be executed from. This folder must contain the necessary files for packaging,
    /// especially the .nuspec file.
    /// </param>
    /// <param name="packageVersion">
    /// The version of the package to be created. This is used by the NuGet CLI for the package's file name, and is therefore necessary to get the correct
    /// output file path.
    /// </param>
    /// <param name="commitId">
    /// The commit identifier to include as a property in the package creation process. This will automatically be embedded in the package metadata.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.
    /// </param>
    /// <returns>
    /// The full path to the created NuGet package file if the operation succeeds; otherwise, <see langword="null"/> if the package creation fails.
    /// </returns>
    public async Task<string?> CreatePackageAsync(string stagingFolder, SemanticVersion packageVersion, string commitId, CancellationToken cancellationToken = default)
    {
        const string packageSubFolder = "package";

        LogCreatingPackage(stagingFolder, packageVersion, packageSubFolder);

        Process packProcess = Process.Start(new ProcessStartInfo()
        {
            FileName = "nuget",
            Arguments = $"pack {_packageManifest.Id}.nuspec -Properties commitId={commitId} -OutputDirectory ./{packageSubFolder}",
            WorkingDirectory = stagingFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        await packProcess.WaitForExitAsync(cancellationToken);

        if (packProcess.ExitCode != 0)
        {
            string errorOutput = await packProcess.StandardError.ReadToEndAsync(cancellationToken);
            LogCreatingPackageFailed(errorOutput);
            return null;
        }

        return Path.Combine(stagingFolder, packageSubFolder, $"{_packageManifest.Id}.{packageVersion}.nupkg");

    }

    /// <summary>
    /// Uploads a NuGet package to the configured package source.
    /// </summary>
    /// <remarks>
    /// This method uses the NuGet CLI to upload the package. Ensure that the NuGet CLI is installed and available in the system's PATH. The method logs the
    /// upload process and any errors encountered. If the NuGet CLI process exits with a non-zero code, the method returns <see langword="false"/>.
    /// </remarks>
    /// <param name="packageFilePath">The full file path of the NuGet package to upload. This must be a valid file path to an existing package file.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns><see langword="true"/> if the package was successfully uploaded; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> UploadPackageAsync(string packageFilePath, CancellationToken cancellationToken = default)
    {
        LogUploadingPackage(packageFilePath);

        string folder = Path.GetDirectoryName(packageFilePath)!;
        string fileName = Path.GetFileName(packageFilePath);

        Process pushProcess = Process.Start(new ProcessStartInfo()
        {
            FileName = "nuget",
            Arguments = $"push {fileName} -Source {_nuGetOptions.Source} -ApiKey {_nuGetOptions.ApiKey}",
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        await pushProcess.WaitForExitAsync(cancellationToken);

        if (pushProcess.ExitCode != 0)
        {
            string errorOutput = await pushProcess.StandardError.ReadToEndAsync(cancellationToken);
            LogUploadingPackageFailed(errorOutput);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Deletes a specific pre-release version of a NuGet package from the configured source.
    /// </summary>
    /// <remarks>
    /// This method only deletes pre-release package versions. If the specified <paramref name="packageVersion"/> is not a pre-release version, the method logs
    /// a message and returns <see langword="false"/> without performing any action. The method uses the NuGet CLI to delete the package. Ensure that the NuGet
    /// CLI is installed and available in the system's PATH. If the NuGet CLI process exits with a non-zero code, the method returns <see langword="false"/>.
    /// </remarks>
    /// <param name="packageVersion">The version of the package to delete. Must be a pre-release version.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will terminate early if the token is canceled.</param>
    /// <returns><see langword="true"/> if the package was successfully deleted; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> DeletePackageAsync(SemanticVersion packageVersion, CancellationToken cancellationToken = default)
    {
        if (!packageVersion.IsPrerelease)
        {
            LogWillNotDeleteNonPrereleasePackageVersions();
            return false;
        }

        LogDeletingPackage(packageVersion);

        Process deleteProcess = Process.Start(new ProcessStartInfo()
        {
            FileName = "nuget",
            Arguments = $"delete {_packageManifest.Id} {packageVersion} -NoPrompt -Source {_nuGetOptions.Source} -ApiKey {_nuGetOptions.ApiKey}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        await deleteProcess.WaitForExitAsync(cancellationToken);

        if (deleteProcess.ExitCode != 0)
        {
            string errorOutput = await deleteProcess.StandardError.ReadToEndAsync(cancellationToken);
            LogDeletingPackageFailed(errorOutput);
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    Task<bool> ICleanUpService.CleanUpAsync(SemanticVersion latestPackageVersion, CancellationToken cancellationToken)
        => DeletePackageAsync(latestPackageVersion, cancellationToken);
}
