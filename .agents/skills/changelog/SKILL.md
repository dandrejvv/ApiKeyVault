---
name: changelog
description: Curate human-friendly release notes in CHANGELOG.md from git commits since the last tag, grouped by Added, Changed, Fixed, Security, and Removed.
---

# Curating Release Notes (`CHANGELOG.md`)

GitHub's `generate_release_notes` only captures merged Pull Requests. For this repository, curated release notes live in `CHANGELOG.md` and are automatically extracted by `.github/workflows/release.yml` into each GitHub Release.

## Workflow

1. **Inspect commits since the previous release tag:**
   ```powershell
   $lastTag = git describe --tags --abbrev=0
   git log "$lastTag..HEAD" --no-merges --oneline
   ```
   Review diffs (`git show <sha> --stat` or full diff when needed) so bullet points describe real user-visible behavior rather than echoing raw commit titles.

2. **Curate concise, user-facing entries:**
   - Group entries under standard headings (include only non-empty sections):
     - `### Added` — new features, UI controls, CLI flags/commands
     - `### Changed` — behavior updates, UX refinements, defaults
     - `### Fixed` — bug fixes and regressions resolved
     - `### Security` — cryptographic, secret-handling, or access-control hardening
     - `### Removed` — dropped options or deprecated behavior
   - Omit pure internal noise (formatting, minor test refactors, badge cache tweaks, release-bump chores) unless relevant to users or contributors.
   - Start each bullet with a bold short topic followed by a crisp description (e.g. `- **Hold-to-Peek Control:** Reveals the full secret only while pressed.`).

3. **Format the version section in `CHANGELOG.md`:**
   - Place the new section directly below `## [Unreleased]` using the exact heading format expected by `release.yml`:
     ```markdown
     ## [X.Y.Z] - YYYY-MM-DD
     ```
   - Move any already-curated items out of `## [Unreleased]` into the new `[X.Y.Z]` section (and leave `## [Unreleased]` empty and ready for future changes).
   - Do **not** manually add a `Full Changelog:` link — `.github/workflows/release.yml` appends it automatically during the GitHub Release step.
