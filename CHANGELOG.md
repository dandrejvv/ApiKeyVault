# Changelog

All notable user-facing changes to **ApiKeyVault** are documented in this file.

## [Unreleased]

## [0.1.1] - 2026-11-27

### Added
- **Masked Secret Ends Preview:** Displays the first 2 and last 2 characters (`ab••••••••yz`) in the key inspector when a secret is at least 12 characters long (≥ 8 hidden characters in between), while fully masking shorter secrets.
- **Hold-to-Peek Control:** Added a `Peek` button next to the 10-second reveal toggle that shows the full secret only while held down and immediately obscures it on release.
- **Wide-View Secret Column:** Automatically shows a `SECRET` preview column in the key table when the list pane is wide enough (≥ 660 px).
- **App Version Badges:** Displays the build version (`vX.Y.Z`) on the Lock Screen and in the bottom Status Strip.

### Changed
- **Tag-Driven Versioning:** Standardized on tag-driven SemVer (`vX.Y.Z`) with a `-dev` local build suffix (`0.1.1-dev`) and shared version formatting across the CLI and desktop UI.

## [0.1.0] - 2026-11-10

### Added
- Initial release of ApiKeyVault desktop UI (Avalonia) and `akv` CLI (`Spectre.Console`)
- XChaCha20-Poly1305 encrypted vault format with Argon2id passphrase derivation, device-bound lockboxes, recovery codes, and conflict merging
- Automated cross-platform builds (`win-x64`, `win-arm64`, and `portable`)
