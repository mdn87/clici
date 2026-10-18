## What this changes

<!-- What behavior differs after this PR, and why. -->

## Why

<!-- The problem being solved. Link an issue if there is one. -->

## Verification

<!--
Say what you actually ran, and what it reported. If you could not run
something, say so plainly rather than leaving it implied.
-->

- [ ] `dotnet build clici.sln -c Release` — 0 warnings, 0 errors
- [ ] `dotnet test clici.sln -c Release` — green
- [ ] Verified by hand on Windows (say what you did)

## Clipboard safety

clici rewrites the clipboard silently, so a wrong decision destroys the user's
copy. If this PR touches classification, normalization, joining, or the write
path:

- [ ] Content that cannot be classified with confidence is left **unchanged**
- [ ] No new path adds a clipboard privacy format the source did not set
- [ ] Rich, non-text, and unknown-format items are still skipped
- [ ] A second pass over the output is a no-op

## Documentation

Per `docs/release-checklist.md`, behavior changes need their descriptions
updated in the same PR:

- [ ] `README.md` — behavior, **Current limitations**, **Planned next steps**
- [ ] `README.md` — configuration example matches the real schema
- [ ] `docs/introductory-slice-spec.md` — NORM/JOIN/PROC/CLIP/CONF requirements
- [ ] `CHANGELOG.md` — entry under **Unreleased**
- [ ] `schemaVersion` incremented if the config schema changed
- [ ] Not applicable
