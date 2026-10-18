# Changelog

All notable changes to clici are documented here.

The format follows [Keep a Changelog][kac], and clici aims to follow
[Semantic Versioning][semver]. clici is pre-1.0: behavior may still change
between minor versions where a classification proves to be wrong.

[kac]: https://keepachangelog.com/en/1.1.0/
[semver]: https://semver.org/spec/v2.0.0.html

## [Unreleased]

### Added

- Optional clipboard image export (`clipboardImageExportPath`, off when empty).
  A clipboard image is written as a PNG to a local or `\\wsl.localhost` path,
  with `clipboardImageExportHistory` timestamped copies (default 20, oldest
  pruned). The clipboard itself is unchanged; monitor-processing exclusions and
  unreadable privacy metadata skip the export, and remote UNC paths are refused
  (#6).
- Claude Code renderer navigation, bundled with the Codex deployment (#33).
  Prompt anchors use case-insensitive nonces (#35, #38), and the plugin no
  longer pins Claude 2.1.296 (#39). Still an opt-in developer bundle; physical
  Terminal qualification remains open (`docs/claude-renderer-qualification.md`).
- Prompt navigation shortcuts moved to Alt+Shift+Comma (previous) and
  Alt+Shift+Period (next) (#30), with PowerShell and bash profile autostart for
  the renderer bundle (#31).
- Experimental Codex pre-Terminal keyboard adapter for Ctrl+Up/Down (NAV-003).
  A native confirmation after each press is required before scrolling; focus,
  pointer, modal, layout and expiry checks cancel stale work. Session-only full
  CLI bundles provide activation and disable without replacing the installed CLI.
  This is limited to local, non-elevated, single-pane Windows Terminal sessions;
  broader qualification, Claude support and tray integration remain open.
- Development-only Codex 0.162.1 renderer patch and private-pipe COM bridge.
  Native message IDs map to independently observed colored blank-row anchors;
  ambiguous or incomplete paint refuses navigation. Native shortcut handling
  remains a separate diagnostic because Terminal processes those keys first.
- Development-only native prompt identity validation, bounded key admission with
  held-key/deadline handling, and navigation-specific arrow parsing (NAV-003,
  NAV-004, NAV-005). A Claude function-hook probe collects IDs without retaining
  prompt text. Navigation remains disabled: a classic-mode renderer/key bridge
  still needs qualification; no input hook or plugin is installed.
- Development-only Windows Terminal COM navigation probe and isolated fixture
  runbook, plus pure previous/next prompt decisions with 24 tests (NAV-001,
  NAV-002, NAV-005). Navigation is not integrated into the tray app. The proof
  report records native-mark failures and remaining agent-native integration work.

### Fixed

- Clipboard copies with no owner window are skipped instead of being
  attributed to the foreground app, so an app re-posting its clipboard on exit
  is no longer normalized (TG-01, #34).
- Contended clipboard reads that WinForms surfaced as missing or empty text are
  retried, and a persistent failure is reported as `UnicodeTextUnreadable`
  (M17, `d691793`).
- A failed Codex or Claude navigation helper is replaced with capped backoff
  instead of disabling navigation, and frames keep drawing until the
  replacement is up (#36, #37, #40).

## 0.1.0 — 2026-08-29

First release. Margin normalization, wrapped-line joining, source-process
attribution, clipboard privacy preservation, the tray application, and a
per-user Windows installer. See the README for the full behavior and its
limitations.

The installer is unsigned, so SmartScreen warns on first run; a `.sha256` is
published beside it.

### Fixed

- Wrapped-line joining no longer inserts a space into a token the terminal
  split by column. Joining now requires positive evidence that the seams are
  word boundaries: content carrying no whitespace at all is refused, as is a
  right edge flush to a single column when two or more non-final lines were
  measured. A refused copy is left unchanged and the `Ctrl+Alt+J` hotkey still
  joins it on request (JOIN-001a).
- The `Ctrl+Alt+J` hotkey chooses its join separator from the same evidence
  rather than always inserting a space. It is documented as the way to recover
  a copy the automatic path refused, but joined with single spaces, so pressing
  it rebuilt the corruption that had just been refused. Word-wrapped fragments
  still rejoin with spaces, a token split mid-word rejoins with nothing, and a
  seam whose neighbours kept their whitespace stays a word boundary either way
  (JOIN-004a).

### Changed

- `.editorconfig` declares `end_of_line = lf`, matching what the repository has
  always stored, and a new `.gitattributes` normalizes the working tree to LF
  on every platform.

### Added

- Contributor documentation: `CONTRIBUTING.md`, `SECURITY.md`,
  `CODE_OF_CONDUCT.md`, issue templates, and a pull request template.
- CI verifies whitespace formatting against `.editorconfig`, and Dependabot
  tracks GitHub Actions and NuGet updates monthly.
- A Release workflow builds the installer on `windows-latest` from a `v*` tag,
  writes a `.sha256` beside it, and creates a draft release, so a published
  binary comes from a known checkout rather than a maintainer's working tree.

