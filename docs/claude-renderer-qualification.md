# Claude renderer integration: 2026-10-17 qualification

**Claude navigation is not live.** The native IDs and blank-row encoding work,
but adding a `UserMessage` render hook changes retained prompt rows in the
classic-mode acceptance test. A one-hook pass-through reproduction fails on both
the installed 2.1.296 and a project-local 2.1.302. No Claude plugin, executable,
shell profile, shortcut, or per-user setting was deployed or changed.

## What was implemented

The [development plugin](../tools/proof/prompt-navigation/claude-renderer/README.md)
keeps native session and message IDs, associates each eligible main-loop composer
prompt with a stable random nonce, and wraps the original drawing with two blank
RGB rows. The encoding matches the existing Clici COM resolver. Prompt text stays
out of the metadata, and all reports explicitly refuse navigation eligibility.

Seven independent registry tests cover duplicate IDs, ordering, stale epochs,
missing paint and capacity. Six native hook-runtime tests cover unchanged stored
content and original drawing, non-main messages, version/enable guards,
fullscreen exclusion and exit. These tests pass; they do not prove paint.

The native API still lacks a read-only main-composer ownership observation.
`$.prompt.read()` returns text/cursor even when they do not establish keyboard
ownership; `$.prompt.fill()` can report a dialog refusal but is a write operation.
Plugin panel focus and `ClientSurface.onKey` do not cover the normal composer.
The requested choice about allowing navigation throughout Claude's active pane,
including dialogs, remains unanswered. That choice cannot resolve the renderer
failure described below.

## Real renderer evidence

All cases use the real native executable in Windows ConPTY, classic mode, three
identical Unicode prompts, 45-line synthetic replies, a nonempty unsent draft,
and a local fake API. The output stream is decoded with pyte; no saved user
conversations or credentials are read. Stock and hooked cases use the same
fixture. Six local HTTP requests include Claude's auxiliary requests; no request
goes to a real model endpoint.

| Case | Retained prompt rows or anchors | Result |
| --- | --- | --- |
| 2.1.296, stock | All three prompt rows | Pass |
| 2.1.296, one pass-through render hook | One of three prompt rows | Fail |
| 2.1.296, anchor plugin | Three native IDs and three render requests, but incomplete emitted marker pairs | Fail |
| 2.1.302, stock | All three prompt rows | Pass |
| 2.1.302, one pass-through render hook | One of three prompt rows | Fail |

A preliminary one-prompt run did emit both endpoints as six colored ASCII
spaces, around the correct native prompt. The repeated-prompt run exposed the
failure the shorter run missed. Removing diagnostic file writes and session-ID
lookups from the render hot path did not resolve it.

The minimal reproduction has no Clici state, message hooks, text changes, file
writes or keyboard handlers:

```typescript
export const register = on => {
    on('ui.render', { component: 'UserMessage' }, (_$, e, next) => next(e));
};
```

This comparison isolates the observed regression to enabling the vendor render
hook in this fixture. It does not establish the internal cause, nor prove that
every ordinary session hits it. A timing or retained-row lifecycle defect is an
inference, not a confirmed implementation diagnosis.

The 2.1.302 executable came from the official npm platform package, unpacked
only under ignored project artifacts. Its tarball matched the registry SHA-512:
`XvgQZTaVb0uu5bN7ZImXuy0iqolox0Or5yAemsgfKJ87183l2FBosSi7wUhIX5dEsGjN41z0vQ1pvrZ39rV/ZA==`.
The regular `claude` command still uses 2.1.296.

Canonical evidence is under `artifacts/prompt-navigation/20261017-claude/`:
`verified-stock-296/`, `verified-noop-296/`, `verified-anchors-296/`,
`verified-stock-302/` and `verified-noop-302/`. Each case preserves the original
VT stream, full-width decoded cells and `console-result.json`. The adapter run
has three render requests but only the first complete pair, `passed: false`, and
the unsent draft preserved.

These final runs use unique assistant response IDs and preserve blank columns
and original line endings. They replace preliminary evidence captured before
those test-harness corrections; the outcome is unchanged. The initial
single-prompt encoding check uses `anchors.json`, `console-output.txt` and
`console-cells.json` at the run root.

A read-only replay attempt in the portable Windows Terminal could not obtain a
`TermControl` TextPattern from the hidden fixture (`textControls: 0`). It is not
counted as Windows Terminal validation. No focus switching was requested from
the operator, and the disposable replay processes were released automatically.

## Requirement before deployment

The stock/no-op control must retain all three prompts, and the actual plugin
must retain complete ordered anchor pairs across repeated prompts and redraws.
Then qualify the existing COM/input bridge against Claude's live pane, with a
concrete decision about composer-only versus whole-pane keys. Missing pairs
must continue to refuse navigation; weakening that check would hide the defect.

The reproducible test and reduced plugin are ready for upstream investigation;
no external issue or message has been posted. A vendor fix or a separately
qualified renderer interface is needed before automatic Claude activation.

Reference: Claude's [mods reference](https://code.claude.com/docs/en/plugins/mods/reference)
and [rendering guide](https://code.claude.com/docs/en/plugins/mods/interface)
document `UserMessage` render hooks and the original drawing returned by `next`.
The installed declaration and live executable are the authority for each tested
version. The [release log](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md)
was checked before testing the newer build.
