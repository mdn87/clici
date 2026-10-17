# Claude renderer qualification

**Not deployed. Do not install this probe in normal Claude sessions.** The
classic-renderer acceptance test fails on Claude Code 2.1.296, and a minimal
pass-through render hook reproduces missing prompt rows on 2.1.296 and 2.1.302.
The full findings are in the [qualification report](../../../../docs/claude-renderer-qualification.md).
No keyboard handler, background service, launcher change, or installer is included.

The experiment joins engine-stamped main-loop composer UUIDs with `UserMessage`
render IDs. It wraps the original drawing with two blank six-cell RGB rows using
the same 128-bit nonce encoding as Clici's COM bridge. IDs and nonces stay outside
the visible text. New row pairs add two blank lines; the original render tree and
stored message are passed through unchanged. It does not read saved conversations.

An explicit `CLICI_CLAUDE_ANCHORS=1` enables the experiment on 2.1.296 only.
`CLICI_CLAUDE_ANCHOR_REPORT` optionally names a development JSON file containing
IDs and nonces, never prompt text. `/clici-renderer` prints the same metadata.
Every report has `navigationEligible: false`; neither a render request nor this
file proves that a row was painted or that Claude owns keyboard input.

**Leaving the environment flag unset is not a workaround for the vendor
reproduction:** merely loading a `UserMessage` hook can affect retained rows.
Normal sessions must not load this plugin at all until that failure is resolved.

The registry retains at most 4,096 IDs. Duplicate text remains distinct by native
ID; repeated draws reuse its nonce. Clear, resume, exit and completed main
compaction invalidate old epochs. Render-only rows do not establish main-loop
membership. Committed prompts are published before their markers paint so that a
missing newest pair cannot silently disappear from the physical map. Append
invocation order is provisional and must agree with actual buffer order. Resumed
history is not captured, and `historyComplete` remains false.

## Automated checks

These checks do not install the plugin or call a model:

```powershell
$env:CLAUDE_CONFIG_DIR = '<repo>/artifacts/claude-check/config'
claude plugin validate --json ./tools/proof/prompt-navigation/claude-renderer
claude plugin test ./tools/proof/prompt-navigation/claude-renderer
node --test ./tools/proof/prompt-navigation/claude-renderer/tests/anchors.node.mjs
```

Type checking uses the installed 2.1.296 declaration under
`.claude-plugin/types/claude-code/index.d.ts`, as for the identity probe. Vendor
declarations are ignored and are not redistributed. The seven Node tests run in
CI. The six native hook tests require Claude 2.1.296 locally; they validate event
handling and returned trees, not retained terminal rows.

## Real renderer acceptance test

`test_console.py` launches an owned Windows ConPTY at 105 columns by 38 rows. It
submits three identical Unicode prompts to Claude's real classic renderer, then
leaves an unsent draft. A local fake Anthropic endpoint supplies 45-line answers.
No credentials, external model calls, desktop key injection, or focus switching
are needed. Settings are isolated; tools, MCP servers, skills and nonessential
traffic are disabled for the fixture. The harness accepts only its own freshly
created test folder and declines external instruction imports.

```powershell
python -m pip install --target <repo>/artifacts/claude-check/deps -r ./tools/proof/prompt-navigation/claude-renderer/requirements-console.txt
python ./tools/proof/prompt-navigation/claude-renderer/test_console.py --run <repo>/artifacts/claude-check/stock --deps <repo>/artifacts/claude-check/deps --claude '<absolute-path-to-claude.exe>' --control
python ./tools/proof/prompt-navigation/claude-renderer/test_console.py --run <repo>/artifacts/claude-check/noop --deps <repo>/artifacts/claude-check/deps --claude '<absolute-path-to-claude.exe>' --noop
```

Run directories must be new so that evidence is not overwritten. Add
`--version 2.1.302` when testing that exact executable. Omit `--control`/`--noop`
to exercise the anchor plugin on 2.1.296. `--passthrough` loads the full plugin
with anchors disabled. Each mode exits nonzero if the expected three prompts or
complete anchor pairs are not retained. Known failing acceptance cases are not
turned into successful tests by expecting missing rows.

The harness saves synthetic output, decoded cells, the final screen and a JSON
result. The anchor case also saves its observed metadata. This is renderer-output
evidence, not Windows Terminal COM, physical keyboard, selection or resize
qualification. The controls intentionally remain outside ordinary CI because
they need a locally installed vendor executable and currently reproduce a defect.
