# Blazor.Radzen.Theming.Updater

A program that updates the [**Blazor.Radzen.Theming**](https://github.com/cytoph/blazor-radzen-theming) [NuGet package](https://www.nuget.org/packages/Blazor.Radzen.Theming/). It automates the process of creating, committing, and releasing updates for the package, including generating necessary files, interacting with GitHub, and publishing to NuGet.

## Arguments and Configuration

This section describes the arguments that can be provided to the program to modify its behavior at runtime, as well as the configuration entries that define its default behavior. Configuration entries are typically set in `appsettings.json` (except for sensitive values like the GitHub token and NuGet API key, which should be provided via `secrets.json` or environment variables). Configuration entries are grouped into categories, and the table below reflects their structure.

### Arguments

The following arguments can be passed to the program to override default behavior without modifying configuration files:

| Argument               | Description                                                                        |
|------------------------|------------------------------------------------------------------------------------|
| `--no-commit`          | Do not create a commit. Implies `--no-release`.                                    |
| `--no-release`         | Do not create a release on GitHub.                                                 |
| `--pack`               | Create a NuGet package.                                                            |
| `--push`               | Push the package to the configured NuGet source. Implies `--pack`.                 |
| `--no-clean-files`     | Do not clean up staging files before the operation.                                |
| `--clean-github`       | Clean up the GitHub repository before the operation.                               |
| `--clean-nuget`        | Clean up the latest NuGet package before the operation.                            |
| `--clean-all`          | Clean up all staging files (default), GitHub repository, and latest NuGet package. |

#### Constraints

- As stated above, `--no-commit` implies `--no-release`, and `--push` implies `--pack`.
- As `--no-commit` prevents creating a commit, it cannot be combined with `--push`; but it can be combined with `--pack` to create a package with a dummy commit ID.

#### Clean-Up Details

- **Files**: Deletes the staging folder to ensure no leftover files from previous runs. This is done by default for safety reasons but can be disabled using the `--no-clean-files` argument.
- **GitHub**: Deletes branches and associated releases from the GitHub repository, except for branches named `main` or `master`. If one attempts to clean a `main` or `master` branch, the program is aborted.
- **NuGet**: Deletes or unlists prerelease packages from the NuGet source. Non-prerelease packages are never deleted. If one attempts to delete a non-prerelease package, the program is aborted.

### Configuration Entries

The "Default/Example Value" column contains default values for optional entries and example values for mandatory entries.

#### General

| Key                | Description                                                                                     | Optional | Default/Example Value |
|--------------------|-------------------------------------------------------------------------------------------------|----------|-----------------------|
| `StartYear`        | Used in conjunction with the current year to determine the validity period for the license.     | No       | `2023`                |
| `Authors`          | The authors of the package, used in license and metadata generation.                            | No       | `cytoph`              |
| `VersionCreationLimit` | Maximum number of versions to create in one execution.                                      | Yes      | `0` (unlimited)       |

#### Package

| Key                     | Description                                                                                    | Optional | Default/Example Value   |
|-------------------------|------------------------------------------------------------------------------------------------|----------|-------------------------|
| `Id`                    | The NuGet package ID.                                                                          | No       | `Blazor.Radzen.Theming` |
| `RepositoryOwner`       | The owner of the package's GitHub repository.                                                  | No       | `cytoph`                |
| `RepositoryName`        | The name of the package's GitHub repository.                                                   | No       | `blazor-radzen-theming` |
| `RepositoryBranchName`  | The branch to use for commits and releases.                                                    | No       | `main`                  |
| `PreReleaseIdentifier`  | Optional pre-release identifier for versioning. Gets suffixed with a dot and a Unix timestamp. | Yes      | `beta`                  |

#### Base Package

| Key                          | Description                                                              | Optional | Default/Example Value  |
|------------------------------|--------------------------------------------------------------------------|----------|------------------------|
| `Id`                         | The NuGet package ID of the base package.                                | No       | `Radzen.Blazor`        |
| `RepositoryOwner`            | The owner of the base package's GitHub repository.                       | No       | `radzen`               |
| `RepositoryName`             | The name of the base package's GitHub repository.                        | No       | `radzen-blazor`        |
| `ContentFolderPath`          | The folder path in the base package repository containing content files. | No       | `Radzen.Blazor/themes` |

#### Staging

| Key                          | Description                                                             | Optional | Default/Example Value |
|------------------------------|-------------------------------------------------------------------------|----------|-----------------------|
| `Folder`                     | The root folder for staging files.                                      | Yes      | `output`              |
| `KeepFolder`                 | Whether to keep the staging folder after execution.                     | Yes      | `false`               |
| `CreateVersionSubfolders`    | Whether to create subfolders for each version in the staging folder.    | Yes      | `true`                |

#### Artifact

| Key                          | Description                                                                                                 | Optional | Default/Example Value |
|------------------------------|-------------------------------------------------------------------------------------------------------------|----------|-----------------------|
| `PackageBuildFolderName`     | The folder name for MSBuild files (like .props/.targets).                                                   | No       | `buildTransitive`     |
| `PackageContentFolderName`   | The folder name for content files. Files from the base package repo's `ContentFolderPath` are put here.     | No       | `themes`              |
| `BuildPropertyPrefix`        | Prefix for build properties in MSBuild files.                                                               | No       | `BRT_`                |
| `ProjectContentFolderName`   | The folder in the `obj` directory of the referencing project where content files are copied during restore. | No       | `Radzen.Blazor`       |

#### GitHub

| Key                          | Description                                                                                     | Optional | Default/Example Value                    |
|------------------------------|-------------------------------------------------------------------------------------------------|----------|------------------------------------------|
| `Token`                      | The GitHub personal access token for authentication.                                            | No       | -                                        |
| `CommitMessageTemplate`      | Template for commit messages.                                                                   | No       | `Update {packageId} to {packageVersion}` |
| `ReleaseNoteTemplate`        | Template for release notes.                                                                     | No       | `Release notes for {packageId}`          |

#### NuGet

| Key                          | Description                                                                                     | Optional | Default/Example Value                 |
|------------------------------|-------------------------------------------------------------------------------------------------|----------|---------------------------------------|
| `Source`                     | The NuGet source URL.                                                                           | No       | `https://api.nuget.org/v3/index.json` |
| `ApiKey`                     | The API key for publishing to NuGet.                                                            | No       | -                                     |

#### Permissions

- **GitHub Token**: Requires read/write permissions to the repository's code.
- **NuGet API Key**: Requires permissions to push new package versions, create new packages (if they don't already exist), and unlist package versions (if clean-up is used).

## Overview

The program begins by retrieving the latest version of the main package (`Blazor.Radzen.Theming`) and all versions of the base package (`Radzen.Blazor`). For each version of the base package that is higher than the latest version of the main package, the program performs the following steps:

1. **Staging Folder Setup**:
   - Creates a staging folder to assemble all necessary files for the new package version.

2. **File Generation**:
   - Generates the license file (`LICENSE`).
   - Generates the README file (`README.md`).
   - Generates the specification file (`.nuspec`) for the NuGet package.
   - Generates the MSBuild file (`.props`).
   - Copies content files from the base package repository.
   - Copies asset files from the `Resources` folder.

3. **GitHub Integration**:
   - Commits the changes to the configured branch (skipped if `--no-commit` is provided).
   - Creates a release on GitHub (skipped if `--no-commit` or `--no-release` is provided).

4. **NuGet Publishing**:
   - Creates a NuGet package using the generated `.nuspec` file (skipped unless `--pack` or `--push` is provided).
   - Publishes the package to the configured NuGet source (skipped unless `--push` is provided).

## Resources Folder Structure

The `Resources` folder contains templates and assets used during the file generation process.

### Templates

The `Templates` folder includes the following files:
- **`LICENSE`**: Used to generate the license file.
- **`README.md`**: Used to generate the README file.
- **`package.nuspec`**: Used to generate the NuGet package specification.
- **`build.props`**: Used to generate MSBuild property files.

### Assets

The `Assets` folder contains static files that are copied as-is to the staging folder. The folder structure within `Assets` is preserved in the staging folder.

## Placeholders

The templates and configuration entries support placeholders that are replaced during execution. Below is a list of placeholders and their usage:

- **`LICENSE`**:
  - `$period$`: Replaced with the license period (e.g., `2023-2025`). (`General:StartYear`)
  - `$authors$`: Replaced with the authors of the package. (`General:Authors`)

- **`README.md`**:
  - `$projectContentFolderName$`: Replaced with the folder name for project content files. (`Artifact:ProjectContentFolderName`)

- **`package.nuspec`**:
  - `$packageId$`: Replaced with the package ID. (`Package:Id`)
  - `$version$`: Replaced with the package version.
  - `$authors$`: Replaced with the authors. (`General:Authors`)
  - `$gitHubAddress$`: Replaced with the GitHub repository URL. (`Package:RepositoryOwner` and `Package:RepositoryName`)
  - `$branch$`: Replaced with the branch name. (`Package:RepositoryBranchName`)
  - `$commitId$`: Replaced with the commit ID, but **only** in the NuGet packaging process.

- **`build.props`**:
  - `_PRFX_`: Replaced with the build property prefix. (`Artifact:BuildPropertyPrefix`)
  - `$packageContentFolderName$`: Replaced with the folder name for content files. (`Artifact:PackageContentFolderName`)
  - `$projectContentFolderName$`: Replaced with the folder name for project content files. (`Artifact:ProjectContentFolderName`)

- **`CommitMessageTemplate`**:
  - `{packageId}`: Replaced with the package ID. (`Package:Id`)
  - `{packageVersion}`: Replaced with the package version.
  - `{basePackageId}`: Replaced with the base package ID. (`BasePackage:Id`)
  - `{basePackageVersion}`: Replaced with the base package version.

- **`ReleaseNoteTemplate`**:
  - `{packageId}`: Replaced with the package ID. (`Package:Id`)
  - `{packageVersion}`: Replaced with the package version.
  - `{basePackageId}`: Replaced with the base package ID. (`BasePackage:Id`)
  - `{basePackageVersion}`: Replaced with the base package version.
  - `{commitMessage}`: Replaced with the commit message.
  - `{basePackageReleaseUrl}`: Replaced with the base package release URL (or a default URL, if not available).

## Disclaimer

This project is not affiliated with Radzen Ltd. in any way. It is an independent program created to automate the process of updating the **Blazor.Radzen.Theming** NuGet package, including generating files, interacting with GitHub, and publishing to NuGet.

Special thanks to the Radzen team for creating and maintaining the excellent **Radzen.Blazor** component library. If you find their components useful, please consider supporting their work through their official channels and documentation at [radzen.com](https://www.radzen.com/).
