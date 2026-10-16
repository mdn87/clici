# Claude prompt identity probe

Development-only function-hooks plugin, tested with Claude Code **2.1.296**.
It records native session/message IDs in memory and exposes them through the
explicit diagnostic command `/clici-prompt-ids`. It does not navigate, bind keys,
change rendered text, read saved conversations, or call a model. The diagnostic
command prints its metadata as a command-output row; it is not a navigation action.

Only a main-loop `session.append` with `door: 'prompt'`, engine-stamped
`origin.kind: 'composer'`, stored `type: 'user'` and no `isMeta` supplies stored
prompt evidence. `UserMessage` render requests with composer origin supply the
native render-instance ID. An ID seen only in a render request is a candidate:
that envelope alone does not prove main-loop membership, paint completion, or a
position in Terminal scrollback. All reports say `navigationEligible: false`.

The ledger retains at most 4,096 IDs, namespaced by the current native session.
It distinguishes duplicate text by ID, merges observations of the same ID,
reports overflow, and rejects late results from older lifecycle epochs. Clear,
resume and completed main compaction discard prior evidence; exit withdraws it.
An unannounced session-ID change or invalid metadata withdraws it too. Observation
order is **not** conversation order, and `coverage: 'observed-only'` never claims
the beginning or end of history. Prompt text is neither retained nor printed.

## Checks

The native test kit runs the real plugin loader and hook machinery against
synthetic engine events. It does not paint a terminal or simulate raw keyboard
delivery. It needs no login, model call, plugin installation, or settings change:

```powershell
# Set this to an isolated directory inside the repository's ignored artifacts/.
$env:CLAUDE_CONFIG_DIR = '<repo>/artifacts/native-probe/config'
claude plugin validate --json ./tools/proof/prompt-navigation/claude-identity
claude plugin test ./tools/proof/prompt-navigation/claude-identity
node --test ./tools/proof/prompt-navigation/claude-identity/tests/ledger.node.mjs
```

The independent ledger tests run on Node 24 in normal CI. The native hook tests
require the installed Claude build and run locally. For type checking, use that
build's generated `.claude-plugin/types/claude-code/index.d.ts` and TypeScript
5.4 or newer with `tsc -p <plugin-directory>`. Generated vendor declarations are
ignored, not redistributed. The qualification run inspected the declaration
embedded in the installed 2.1.296 executable because the headless loader did not
write it; it has SHA-256
`cf4afc180ffef03cb8138916c0e8f49b8186242a81e226f01ba24341018e5e90`.

For an explicit disposable interactive experiment, `claude --plugin-dir <path>`
loads this directory for that session. This is a test option, not a required
replacement for normal launch commands. Do not install the probe globally as a
working navigation feature. No user settings or installed plugins were changed
in the qualification run.

## Missing native capabilities

In the tested API, `ui.press` is a plugin Button/link event and `ui.input` is a
plugin Input event. `ClientSurface.onKey` only receives keys while that plugin's
region holds focus. `Button.action` can use existing engine actions; it does not
register a new composer-wide action. None supplies a qualified Ctrl+Up/Down
handler from the normal composer. `UserMessage.onScreen` is absent in classic
mode and native transcript scrolling requires a scrollable renderer.

See the [native bridge contract](../../../../docs/prompt-navigation-native-contract.md)
for the remaining renderer work. This probe deliberately leaves IDs disconnected
from COM positions and from the input queue until those facts can be established.
