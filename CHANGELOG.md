# Changelog

All notable changes to clici are documented here.

The format follows [Keep a Changelog][kac], and clici aims to follow
[Semantic Versioning][semver]. clici is pre-1.0: behavior may still change
between minor versions where a classification proves to be wrong.

[kac]: https://keepachangelog.com/en/1.1.0/
[semver]: https://semver.org/spec/v2.0.0.html

## [Unreleased]

### Fixed

- Wrapped-line joining no longer inserts a space into a token the terminal
  split by column. Joining now requires positive evidence that the seams are
  word boundaries: content carrying no whitespace at all is refused, as is a
  right edge flush to a single column when two or more non-final lines were
  measured. A refused copy is left unchanged and the `Ctrl+Alt+J` hotkey still
  joins it on request (JOIN-001a).

### Changed

- `.editorconfig` declares `end_of_line = lf`, matching what the repository has
  always stored, and a new `.gitattributes` normalizes the working tree to LF
  on every platform.

### Added

- Contributor documentation: `CONTRIBUTING.md`, `SECURITY.md`,
  `CODE_OF_CONDUCT.md`, issue templates, and a pull request template.
- CI verifies whitespace formatting against `.editorconfig`, and Dependabot
  tracks GitHub Actions and NuGet updates monthly.

## 0.1.0

Not yet released. The initial release covers margin normalization, wrapped-line
joining, source-process attribution, clipboard privacy preservation, the tray
application, and a per-user Windows installer. See the README for the full
behavior and its limitations.
