# Versioning Strategy

This document explains the versioning strategy for the COA CodeSearch MCP Server.

## Version Format

The project uses semantic versioning: `MAJOR.MINOR.PATCH[-SUFFIX]`

- **MAJOR**: Breaking changes
- **MINOR**: New features, backwards compatible
- **PATCH**: Bug fixes, backwards compatible
- **SUFFIX**: Pre-release identifiers (preview, rc, alpha)

## Automatic Version Management

The version is **automatically managed** by the CI/CD pipeline in `azure-pipelines.yml`:

- The `majorVersion` and `minorVersion` are set in the pipeline variables
- The `patchVersion` is auto-incremented using Azure DevOps counter
- **DO NOT** set version in the .csproj file

## Branch Strategy

### Main Branch
- Produces stable releases: `1.3.0`, `1.3.1`, etc.
- Automatically published to Azure DevOps feed
- No suffix added

### Develop Branch  
- Produces preview releases: `1.3.0-preview.123`
- Published to Azure DevOps feed for testing
- Suffix: `preview.{buildNumber}`

### Feature Branches
- Produces alpha builds: `1.3.0-alpha.feature.123`
- **NOT published to feed** (only build artifacts)
- Suffix: `alpha.{branchName}.{buildNumber}`

### Release Branches
- Produces release candidates: `1.3.0-rc.123`
- **NOT published to feed** (only build artifacts)
- Suffix: `rc.{buildNumber}`

### Tags
- Uses exact version from tag: `v1.3.0`
- Automatically published to Azure DevOps feed
- Tag format: `v{MAJOR}.{MINOR}.{PATCH}`

## Publishing Rules

All branches (except pull requests) publish to the Azure DevOps feed:

1. **Main branch**: Publishes stable versions (e.g., 1.3.0, 1.3.1)
2. **Develop branch**: Publishes preview versions (e.g., 1.3.0-preview.123)
3. **Feature branches**: Publishes alpha versions (e.g., 1.3.0-alpha.feature.123)
4. **Release branches**: Publishes RC versions (e.g., 1.3.0-rc.123)
5. **Tags**: Publishes exact version from tag
6. **Pull requests**: Only build and test (not published)

## How to Update Version

### Patch Version (Bug Fixes)
- **Fully automatic** - increments on EVERY build of main branch
- Uses Azure DevOps counter function
- Counter auto-increments: 1.3.0 → 1.3.1 → 1.3.2
- No manual intervention needed
- The counter is persistent across builds

### Minor Version (New Features)
1. Update `minorVersion` in `azure-pipelines.yml`
2. Reset counter by changing the counter key format
3. Example: 1.3.x → 1.4.0

### Major Version (Breaking Changes)
1. Update `majorVersion` in `azure-pipelines.yml`
2. Reset `minorVersion` to 0
3. Example: 1.x.x → 2.0.0

## Example Scenarios

- Feature branch `feature/new-tool`: Builds `1.3.0-alpha.newTool.456` (not published)
- Develop branch: Builds and publishes `1.3.0-preview.789`
- Main branch: Builds and publishes `1.3.0`
- Tag `v1.3.1`: Builds and publishes exactly `1.3.1`