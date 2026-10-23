# Claude renderer plugin

This plugin gives Clici native prompt identity and composer-ownership evidence
for Claude Code sessions drawn in the classic (non-alternate)
terminal screen. It ships inside the renderer bundle that the
[Codex runbook](../codex-renderer/README.md) installs; the installer adds the
bundled copy to `CLAUDE_CODE_PLUGIN_DIRS` for new Windows Terminal shells, so an
ordinary `claude` command loads it. Building Clici installs nothing.

The earlier "renderer failure" recorded in the
[qualification report](../../../../docs/claude-renderer-qualification.md) was a
fixture artifact: the fake model endpoint answered in 0 ms, and a hooked
`UserMessage` draws at height 0 until the asynchronous render answer settles.
With a 500 ms reply the stock, pass-through and full plugin cases all retain
three prompts and three complete ordered anchor pairs. `--reply-delay 0`
reproduces the old failure on purpose.

## What it does

- `session.append` pins the engine-stamped UUID of each eligible main-loop
  composer prompt and gives it a random 128-bit nonce, written as uppercase
  hex. Nonce case carries no identity: `RenderedPromptMap.Resolve` matches
  nonces case-insensitively and treats two nonces equal apart from case as
  duplicates. Before PR #35 the plugin sent lowercase and Resolve accepted only
  uppercase, so every frame failed as "Incomplete or ambiguous rendered
  anchors".
- `ui.render` for `UserMessage` wraps the original drawing in two blank six-cell
  RGB rows that encode the nonce, using the same encoding as the Codex bridge and
  Clici's COM resolver. IDs and nonces never enter the visible text; the stored
  message and original tree pass through unchanged.
- When `CLICI_RENDERER_BRIDGE` names `PromptNavigation.Probe.exe`, `session.start`
  spawns `renderer-bridge-frames <frame file>` under `%TEMP%\clici-renderer\`.
  The plugin writes a JSON frame there every 100 ms and on every state change;
  the helper discovers its parent process itself, refuses frames older than
  750 ms, and feeds them to the same lease and keyboard adapter Codex uses.
- Passive history discovery runs at most once per second. Once qualified, a
  background helper does not scan the foreground pane. Ordinary typing revokes
  input eligibility but retains the verified layout; renewal checks a painted
  anchor witness before reusing it. These protections also apply to legacy
  frame transport and do not require Codex's v2 admission mapping.
- Every frame is an `observe` request; the plugin never sends a direction. The
  helper's own low-level keyboard hook accepts Alt+Shift+, and Alt+Shift+.
  only while the lease from a recent frame is valid, and the next frame
  collects the accepted press through `TakeConfirmed`. `CLICI_NATIVE_KEY_PROBE` is a separate Ctrl+Up/Down
  diagnostic and plays no part here.
- If the helper exits, fails to start, or a frame write fails, keys pass
  through and the plugin replaces the helper the way the Codex bridge does:
  after 1 s, doubling to a 30 s cap, at most ten times a session (the count
  resets at `session.start`). Each replacement gets a new frame file, so the
  old helper's lease (750 ms from its last frame) has expired before the new
  one starts; prompts already painted in the scrollback stay navigable. Session
  end tears the helper down without replacing it.
- `composerOwnsInput` is true only while every item of evidence agrees: anchors
  active and not overflowed, a `UserMessage` drew in a non-fullscreen terminal
  viewport, no tool call is awaiting a permission or question dialog, and no
  other slash command has opened a panel since the last accepted prompt. A
  withdrawn frame carries no prompts, so the helper passes keys through.
- Clear, resume and exit invalidate the epoch. Compaction keeps the bindings:
  the painted prompts stay in the scrollback, and the helper resolves the newest
  anchors still painted.
  Resumed history is not captured; `historyComplete` stays false.

`CLICI_CLAUDE_ANCHORS=1` enables it on any engine version; it was built and
tested on 2.1.296. It is deliberately not pinned: if an update changes the
drawing, the helper cannot resolve the painted rows and passes the keys through,
and the plugin gets fixed then. `CLICI_CLAUDE_ANCHOR_REPORT`
optionally names a development JSON file with IDs and nonces, never prompt text.
`/clici-renderer` prints the same metadata plus `composerOwnsInput` and whether a
bridge child is alive. Every report has `navigationEligible: false`: the helper
alone decides eligibility from painted rows read through COM.

Known gaps: a permission dialog is inferred from `tool.check` verdicts, so a
tool whose dialog the engine opens without a verdict keeps navigation withdrawn
until the call settles, and the keys do nothing while an approval or question
dialog is open (composer-only capture; whole-pane capture is not approved); a
slash-command panel withdraws navigation until the
next accepted prompt; Esc-Esc rewind and transcript overlays are not observable
through the mods API and are not detected. A frame file outlives a helper killed
with its console; each new helper sweeps siblings older than an hour.

## Automated checks

These checks do not install the plugin or call a model:

```powershell
$env:CLAUDE_CONFIG_DIR = '<repo>/artifacts/claude-check/config'
claude plugin validate --json ./tools/proof/prompt-navigation/claude-renderer
claude plugin test ./tools/proof/prompt-navigation/claude-renderer
node --test ./tools/proof/prompt-navigation/claude-renderer/tests/anchors.node.mjs
```

Type checking uses the installed 2.1.296 declaration under
`.claude-plugin/types/claude-code/index.d.ts`. Vendor declarations are ignored
and not redistributed. The eight Node tests run in CI. The twenty-one native hook
tests require Claude Code locally (`claude plugin test`); they cover event handling, returned trees,
frame publication, ownership withdrawal and release, and helper exit and
replacement, not retained terminal rows.

## Real renderer acceptance test

`test_console.py` launches an owned Windows ConPTY at 105 columns by 38 rows. It
submits three identical Unicode prompts to Claude's real classic renderer, then
leaves an unsent draft. A local fake Anthropic endpoint supplies 45-line answers
after `--reply-delay` seconds (default 0.5). No credentials, external model
calls, desktop key injection, or focus switching are needed. Settings are
isolated; tools, MCP servers, skills and nonessential traffic are disabled. The
harness accepts only its own freshly created test folder and declines external
instruction imports.

```powershell
python -m pip install --target <repo>/artifacts/claude-check/deps -r ./tools/proof/prompt-navigation/claude-renderer/requirements-console.txt
python ./tools/proof/prompt-navigation/claude-renderer/test_console.py --run <repo>/artifacts/claude-check/stock --deps <repo>/artifacts/claude-check/deps --claude '<absolute-path-to-claude.exe>' --control
python ./tools/proof/prompt-navigation/claude-renderer/test_console.py --run <repo>/artifacts/claude-check/anchors --deps <repo>/artifacts/claude-check/deps --claude '<absolute-path-to-claude.exe>'
python ./tools/proof/prompt-navigation/claude-renderer/test_console.py --run <repo>/artifacts/claude-check/bridge --deps <repo>/artifacts/claude-check/deps --claude '<absolute-path-to-claude.exe>' --bridge <bundle>/helper/PromptNavigation.Probe.exe --plugin-env <bundle>/claude-plugin
```

Run directories must be new so that evidence is not overwritten. `--control`
runs stock Claude, `--noop` a one-hook pass-through, `--passthrough` the plugin
with anchors disabled. `--bridge` makes the plugin spawn the named helper with
`CLICI_RENDERER_REPORT` set; the run then also requires the helper's last observe
request to name the fixture's engine process, the same session ID and prompt
bindings as the anchor report, `composerOwnsInput: true` and a lease no longer
than 750 ms. `--plugin-env` loads the plugin through `CLAUDE_CODE_PLUGIN_DIRS`,
the route a deployed shell uses. Each mode exits nonzero when the expected rows,
pairs or bridge checks are missing; failing cases are never made to pass by
expecting missing rows.

The harness saves synthetic output, decoded cells, the final screen and a JSON
result. This is renderer-output and helper-protocol evidence, not Windows
Terminal COM, physical keyboard, selection or resize qualification. The
`--bridge` case starts the helper without `CLICI_RENDERER_KEYS`, so it never
reaches `RenderedPromptMap.Resolve` or the keyboard hook; that gap is why the
lowercase-nonce defect passed it. The helper's frame-file path is covered by
`codex-renderer/test_live_input.py --frames`, which publishes lowercase nonces
through `renderer-bridge-frames` in a portable proof Terminal and drives marked
injected keys through the hook, observe and TakeConfirmed (it fails on the
pre-fix Resolve with "Incomplete or ambiguous rendered anchors"). Matt checked the physical keys in a real
Windows Terminal pane on 2026-10-17 after PR #35: navigation works, and a draft,
the scrollback, a selection and the clipboard survive it. The cases
stay outside ordinary CI because they need a locally installed vendor executable.
