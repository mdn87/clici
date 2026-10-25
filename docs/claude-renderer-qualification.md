> Retired on October 25, 2026. This document is historical; activation and further qualification are cancelled. See the [retirement record](prompt-navigation-retirement.md).

# Claude renderer integration: 2026-10-17 qualification

**Claude navigation is bundled and installable; physical Windows Terminal
keyboard qualification is still open.** The isolated renderer failure below
depends on timing; passing delayed-reply fixtures do not establish reliable rendering in real sessions. The plugin now
publishes composer-ownership frames to the same helper, lease and keyboard
adapter that Codex uses, and the renderer bundle installer loads it for new
Windows Terminal shells. Nothing in this repository changes a user profile or
setting by itself; the installer does so only when run.

## Root cause of the earlier failure

The acceptance fixture's fake endpoint answered in 0 ms. A hooked `UserMessage`
draws at height 0 until the asynchronous `ui.render` answer settles, and an
instant reply redrew the transcript before that happened, so the retained rows
lost the prompt. This exposes a race in asynchronous rendering; it cannot be
dismissed solely because real model replies usually take longer. With a 500 ms reply
the stock, pass-through and full plugin cases all pass; `--reply-delay 0`
still reproduces the old result. The 2026-10-17 morning evidence under
`artifacts/prompt-navigation/20261017-claude/verified-*-296` was captured at 0 ms
and stands as the reproduction, not as a renderer verdict.

| Case (2.1.296, classic, three identical Unicode prompts) | Reply delay | Result |
| --- | --- | --- |
| one pass-through render hook | 0 ms | one of three prompt rows retained |
| one pass-through render hook | 500 ms | three prompt rows retained |
| anchor plugin | 500 ms | three native IDs, three complete ordered anchor pairs, draft preserved |
| anchor plugin plus bundled helper (`--bridge`) | 500 ms | as above, and the helper's last observe request names the engine process, session, bindings, ownership and a 750 ms lease |
| bundled plugin loaded through `CLAUDE_CODE_PLUGIN_DIRS` plus bundled helper | 500 ms | as above |

The runs are under `artifacts/prompt-navigation/20261017-claude/` as
`verified-noop-delay0/`, `verified-noop-delay500/`, `verified-anchors-delay500/`,
`verified-bridge-delay500/` and `verified-bundle-env-delay500/`. Each preserves
the original VT stream, decoded cells, `console-result.json` and, where it
applies, the anchor and bridge reports.

## What was implemented

The [plugin](../tools/proof/prompt-navigation/claude-renderer/README.md) keeps
native session and message IDs, gives each eligible main-loop composer prompt a
random nonce, and wraps the original drawing with two blank RGB rows in the
encoding the Clici COM resolver already reads. Prompt text stays out of every
report.

Transport: Claude plugins cannot stream to a child's stdin, so the plugin
spawns `PromptNavigation.Probe.exe renderer-bridge-frames <file>` and writes a
JSON frame to a private file under `%TEMP%\clici-renderer\` every 100 ms and on
every state change. The helper discovers its parent through
`NtQueryInformationProcess`, requires the parent to predate itself, converts the
frame's wall-clock `observedAt` to a tick lease (refusing frames older than
750 ms or more than one second in the future), and dispatches an observe
request with `AgentKind.ClaudeCode` through the existing `RendererBridge`.
Prompt identity, painted-row verification, navigation state and key handling
are unchanged from Codex.

Composer ownership is a conjunction of evidence: anchors active and not
overflowed, a `UserMessage` drew in a non-fullscreen terminal viewport, no tool
call is awaiting a permission or question dialog (a non-`ask` `tool.check`
verdict releases it early, except for `AskUserQuestion` and `ExitPlanMode`),
and no other slash command has opened a panel since the last accepted prompt.
Session end, fullscreen draws and helper exit withdraw it. A withdrawn frame
carries no prompts, so the helper passes every key through.

Tests: three `RendererFrameClock` facts, fourteen native hook tests (frame
publication, withdrawal and release, helper exit, no spawn without a helper
path), seven registry tests, five installer tests. Unit and fixture success is
not keyboard acceptance.

## Deployment

`Publish-RendererBundle.ps1` copies the plugin into `claude-plugin/` inside the
bundle, `install_renderer.py` requires those files, and the managed profile
blocks export `CLAUDE_CODE_PLUGIN_DIRS`, `CLICI_CLAUDE_ANCHORS=1` and
`CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1` next to the existing Codex variables.
`Enable-RendererNavigation.ps1` sets and restores the same three, and its
`-Disable` stops helpers launched by either CLI beneath that shell. A bundle
built this way was installed against disposable profiles, dot-sourced in a
throwaway shell, and exercised by the fixture through `CLAUDE_CODE_PLUGIN_DIRS`.

## Still open

- Physical Windows Terminal qualification: Alt+Shift+Comma and
  Alt+Shift+Period pressed in a real pane running `claude`, with scrollback,
  a draft and a selection, have not been observed. ConPTY evidence proves
  emitted cells and helper protocol, not COM scrolling or keyboard scope. The
  earlier hidden-Terminal replay (`textControls: 0`) is not repeated.
- Composer ownership stays an inference from hook events. The mods API offers
  no read-only composer-focus observation, and whole-pane capture (including
  dialogs) was never approved, so the plugin withdraws on any evidence of a
  dialog or panel rather than capturing through it.
- Esc-Esc rewind, transcript overlays and engine dialogs that open without a
  `tool.check` verdict are not observable; the first two are undetected, the
  last keeps navigation withdrawn until the call settles.
- `historyComplete` remains false; resumed history is not captured.

Reference: Claude's [mods reference](https://code.claude.com/docs/en/plugins/mods/reference)
and [rendering guide](https://code.claude.com/docs/en/plugins/mods/interface).
The installed 2.1.296 declaration and live executable are the authority.

## October 24 actual-session failure

The operator identified **Lorem ipsum page filler** as the Claude test session.
Read-only COM inspection found three native prompts, two complete marker pairs,
and only the start marker for the newest prompt. The next blank row contained
default foreground colors. A fourth turn retained both markers, but the helper
still refused because of the older damage. The helper now admits only the
contiguous complete recent turns after a gap and stops at that boundary; it never
navigates to the damaged turn. Missing newest markers, duplicate paint and wrong
order still refuse. The cause of the lost marker remains unresolved; see the
[latency investigation](prompt-navigation-latency-plan.md#actual-session-result-and-separate-claude-rendering-failure).
