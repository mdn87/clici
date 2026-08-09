# clici release checklist

Run this before tagging a release. Its purpose is to keep the documentation and
behavior in sync — most past drift came from changing code without updating every
place that describes it.

## Documentation sync

Confirm these all describe the same behavior for the release:

- [ ] `README.md` — normalization rules, source attribution, privacy policy,
      rich-text handling.
- [ ] `README.md` — **Current limitations** matches what actually ships.
- [ ] `README.md` — **Planned next steps** does not list work already done.
- [ ] `README.md` — the configuration JSON example matches the real schema
      (property names, defaults) and `schemaVersion`.
- [ ] `docs/introductory-slice-spec.md` — NORM/PROC/CLIP/CONF requirements and
      the acceptance criteria match the code.
- [ ] `docs/v0.1-test-runbook.md` — operator steps match current behavior.
- [ ] `docs/v0.1-resilience-report.md` — findings still hold.
- [ ] `tools/proof/fixtures.json` — proof fixtures match the normalizer.

## Configuration

- [ ] `schemaVersion` incremented when the config schema changed, with older
      files still loading (unknown/removed fields ignored, invalid values fall
      back).

## Behavior and build

- [ ] `dotnet build clici.sln -c Release` is clean (0 warnings, 0 errors).
- [ ] `dotnet test clici.sln -c Release` is green.
- [ ] Installer behavior in `docs/installer-test-runbook.md` verified
      (install, Start-with-Windows toggle, uninstall).

## Privacy invariants (must hold every release)

- [ ] A rewrite never adds `CanIncludeInClipboardHistory` or
      `CanUploadToCloudClipboard` when the source did not set them.
- [ ] Explicit source privacy values are carried through unchanged.
- [ ] Items with `ExcludeClipboardContentFromMonitorProcessing` are skipped.
- [ ] Rich, non-text, and unknown-format items are skipped in automatic mode.
