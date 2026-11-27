---
name: release
description: Release a new version of ApiKeyVault using tag-driven SemVer (Option A), curating CHANGELOG.md release notes, bumping Directory.Build.props, verifying tests, and pushing the release tag.
---

# ApiKeyVault Release Process

ApiKeyVault uses **tag-driven SemVer (`vX.Y.Z`)** with a `-dev` local fallback in `Directory.Build.props` and curated release notes sourced from `CHANGELOG.md`.

## Version & Release Notes Source of Truth

- **Release builds (CI):** Pushing a git tag `vX.Y.Z` triggers `.github/workflows/release.yml`, which:
  1. Strips `v` from the tag and passes `-p:Version=X.Y.Z` to `scripts/publish.ps1`.
  2. Extracts the `## [X.Y.Z]` section from `CHANGELOG.md` (and appends the `Full Changelog` compare link) for the GitHub Release body.
- **Local / dev builds:** `Directory.Build.props` defines `<Version Condition="'$(Version)' == ''">X.Y.Z-dev</Version>`. Both `akv --version` and the desktop UI (`AppVersion`) read this assembly version metadata (stripping any `+commit` suffix).

## Release Checklist

1. **Check workspace state:**
   - Run `git status` and ensure there are no unintended uncommitted changes.
   - Verify the target version `X.Y.Z` does not already have a git tag (`git tag -l vX.Y.Z`).

2. **Curate release notes in `CHANGELOG.md`:**
   - Follow the `changelog` skill (`.agents/skills/changelog/SKILL.md`) to review commits since the last tag and add a curated `## [X.Y.Z] - YYYY-MM-DD` section under `## [Unreleased]`.

3. **Bump the fallback version:**
   - Update `<Version Condition="'$(Version)' == ''">X.Y.Z-dev</Version>` in `Directory.Build.props`.

4. **Run the full Release test suite:**
   ```powershell
   dotnet test ApiKeyVault.slnx -c Release -p:UseSharedCompilation=false --verbosity minimal
   ```
   All test projects (`ApiKeyVault.Core.Tests`, `ApiKeyVault.Cli.Tests`, `ApiKeyVault.UI.Tests`) must pass with zero errors.

5. **Commit, tag, and push:**
   ```powershell
   git add CHANGELOG.md Directory.Build.props # plus any release-prep files
   git commit -m "chore(release): prepare vX.Y.Z"
   git tag vX.Y.Z
   git push origin main vX.Y.Z
   ```

6. **Verify CI release:**
   - Pushing `vX.Y.Z` triggers `.github/workflows/release.yml`, which re-runs tests, builds `win-x64`, `win-arm64`, and `portable`, and publishes the GitHub Release with the curated `CHANGELOG.md` section.
