# Codex renderer bridge experiment

This is an experimental, opt-in source patch for Codex **0.162.1**, commit
`092d3acd6bec3e3a14bdc7e7a2810ab628ab759d` (`rust-v0.162.1`). It carries native
user-message IDs into the classic scrollback renderer and sends metadata to a
private child of that process. A pre-Terminal keyboard adapter connects
Alt+Shift+Comma / Alt+Shift+Period to the live renderer. Nothing is installed or
enabled by building the project. The session activation script selects a bundled
full `codex` executable for one shell; the installed CLI and shell profiles remain unchanged.
The same bundle carries the [Claude renderer plugin](../claude-renderer/README.md)
under `claude-plugin/`, which gives Claude Code sessions the same
navigation through a frame file instead of pipes.

## Enable automatically in new terminals

On Windows with Python 3.11 or newer, deploy a published bundle once:

```powershell
python ./install_renderer.py --bundle <bundle-directory>
```

The installer verifies every bundled file, copies it into a versioned directory
under `%LOCALAPPDATA%/Clici/renderer-navigation`, and adds managed startup blocks
to the current user's PowerShell 7 profile and Git Bash `.bashrc`. It sets
`tui.alternate_screen = "never"` in the user's Codex config so plain `codex`
uses classic scrollback. The previous settings are backed up in the installation
directory. Existing profile content is preserved; a repeated install is harmless.
The blocks also export `CLAUDE_CODE_PLUGIN_DIRS` (prepending the bundled
`claude-plugin/`), `CLICI_CLAUDE_ANCHORS=1` and
`CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`, so an unchanged `claude` command loads
the plugin in the classic screen. The installed Claude CLI is not replaced.

Open a new Windows Terminal tab, then run `codex`, the existing `cx` alias, or
`claude`. No activation command or additional flags are needed. The automatic
loader requires Windows Terminal, honors `NO_COLOR`, and skips shells started by
a Codex agent. Unsupported and elevated panes still receive no navigation lease.
The installed npm Codex CLI remains on disk, but the terminal uses the pinned,
patched **Codex 0.162.1** while this deployment is enabled. npm updates do not
update this bundle. The Claude plugin is not pinned to a Claude version;
if an upgrade breaks it, the keys pass through until it is fixed. The Codex desktop app is not changed.

To stop automatic loading, run this and open a new terminal tab:

```powershell
python "$env:LOCALAPPDATA/Clici/renderer-navigation/install_renderer.py" --disable
```

Disable removes only the managed profile blocks and restores the prior screen
setting if the user has not replaced it. Later unrelated profile/config edits
are preserved. Existing sessions continue until closed; bundles and backups are
retained. To update the pinned deployment, disable it, then install the new
bundle with the repository's installer. Use `--powershell-profile`,
`--bash-profile`, or `--config` for nonstandard locations.

## What the bridge establishes

- Committed and replayed prompts retain `UserMessageItem.id`. Optimistic paint
  joins its `client_id` to the native receipt before publishing an identity.
  Repeated text and multiple user messages in one turn remain distinct.
- Each rendered user cell has a random nonce. Six ASCII spaces with six RGB
  foreground colors encode it in each of the existing blank separator rows.
  Different end tags distinguish the start and end. No ID or hidden Unicode
  character is put into the prompt. These are explicitly painted blank spaces;
  consumers that preserve trailing whitespace may retain them when copying a
  separator. Prompt text and the visible text grid stay unchanged.
- The native history wrapper preserves these two rows. The COM client reads
  the actual foreground attributes and requires a complete, unique pair for
  every retained prompt. A request to paint does not establish a position.
- Discovery and scrolling both use UIA physical line units. Counting newlines
  in `GetText` is insufficient because Terminal joins soft-wrapped rows.
- The observed pairs must match a suffix of the native prompt list. An evicted
  prefix becomes a capture boundary. Missing interior/newest prompts, repeated
  IDs/nonces, partial paint, duplicate paint, and invalid order refuse navigation.
- Metadata uses private stdin/stdout pipes, contains no prompt text, and carries
  process/session identity, sequence, epoch and a monotonic deadline. A background
  supervisor enforces deadlines independently of the pipe worker. Native input
  only replaces the latest pending observation; it never waits for a reply.
- Protocol v2 adds an unnamed, read-only child mapping containing one atomic
  admission epoch/eligibility word. Revocation bypasses the observation mailbox.
  Queued older confirmations cannot authorize a scroll after a modal/focus/input
  change. The helper starts hooks only after the parent assigns its kill-on-close
  Job and sends the private mapping handshake. Legacy Claude keeps protocol v1
  and its existing input/cache invalidation rules.

The companion helper defaults to explicitly named portable `CLICI-PROOF-*`
Windows Terminal windows. `CLICI_RENDERER_TERMINALS=1` opts into ordinary,
non-elevated Windows Terminal windows with a single pane. Its bound is 4,096 prompts/rows,
1 MiB per native request, 1,048,576 characters per COM capture, and at most one
second per request. It validates the
foreground pane and document again before `ScrollIntoView`, checks selection
and viewport readback, and commits the cursor only after verification. A failed
readback after a possible scroll consumes the request; it never replays a key.

## Build in a separate checkout

Requirements: Windows, .NET 10, Git, Rust with MSVC build tools. The upstream
toolchain pins Rust 1.95; this experiment was compiled with installed Rust 1.99
using `+stable`. Keep Cargo's home and target paths short on Windows and enable
Git long paths in the separate checkout if needed.

```powershell
git clone --branch rust-v0.162.1 --depth 1 https://github.com/openai/codex.git artifacts/codex-renderer
./tools/proof/prompt-navigation/codex-renderer/Apply-CodexBridge.ps1 artifacts/codex-renderer
dotnet build tools/proof/prompt-navigation/PromptNavigation.Probe.csproj -c Release -o artifacts/renderer-helper
Push-Location artifacts/codex-renderer/codex-rs
cargo +stable test -p codex-tui --lib clici_ --locked
cargo +stable build -p codex-tui --bin codex-tui --locked
cargo +stable build -p codex-cli --bin codex --locked
cargo +stable build -p codex-code-mode-host --bin codex-code-mode-host --locked
Pop-Location
```

Run Cargo from `codex-rs` so its MSVC configuration is applied. The patch also
repairs the tag's 161 workspace versions in `Cargo.lock` from `0.0.0` to
`0.162.1`; external dependencies are unchanged. The script requires the exact
clean base commit and checks the patch before modifying that separate checkout.
Patched upstream files retain the [Codex Apache-2.0 license](https://github.com/openai/codex/blob/rust-v0.162.1/LICENSE).

Inside a disposable portable proof Terminal, set `CLICI_RENDERER_BRIDGE` to the
absolute `PromptNavigation.Probe.exe` path and run the built `codex-tui.exe`
with `--no-alt-screen --no-daemon`. The opt-in is inherited by that process only.
`NO_COLOR` disables the native bridge: suppressed RGB output cannot establish
anchors. The live experiment uses a separate color-enabled proof process.
Optionally set `CLICI_RENDERER_REPORT` to an explicit evidence-file path: the
helper writes the latest passive metadata frame there. Inspect its painted
anchors with `renderer-inspect HWND report.json`.

Standalone protocol tests do not require a Codex checkout:

```powershell
cargo +stable test --manifest-path tools/proof/prompt-navigation/codex-renderer/Cargo.toml --locked
```

## Connect a shell session

Publish a new local bundle from the built full CLI and helper. The script also
copies the Claude plugin's manifest, hook registry and two hook modules into
`claude-plugin/`; the installer and the activation script refuse a bundle
without them.

```powershell
./Publish-RendererBundle.ps1 -NativeExecutable <target/debug/codex.exe> `
  -HelperDirectory <renderer-helper> -SourceDirectory <patched-codex-source> `
  -Destination <new-bundle-directory>
. ./Enable-RendererNavigation.ps1 -BundleDirectory <new-bundle-directory>
codex --no-alt-screen
```

Use a local, non-elevated PowerShell 7 session in Windows Terminal. The script
verifies the bundle hashes, selects the full native CLI through that shell's PATH,
and sets `CLICI_RENDERER_BRIDGE`, `CLICI_RENDERER_KEYS=1`,
`CLICI_RENDERER_TERMINALS=1`, `CLAUDE_CODE_PLUGIN_DIRS`, `CLICI_CLAUDE_ANCHORS=1`
and `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`; `claude` started from that shell
loads the bundled plugin. No launch wrapper or alternate-screen renderer is
introduced. `NO_COLOR` must be absent because the anchors require RGB attributes.
The tray application's navigation option remains unimplemented.

Dot-source the same script with `-Disable` to restore this shell's environment
for future children. Running sessions keep their inherited environment; restart
them with the bridge disabled to stop navigation. Killing a helper alone is not
a disable operation: the supervisor can replace it. The tray Enabled checkbox
still controls clipboard cleanup, not renderer navigation.

## Input ownership and remaining qualification

Passive publication remains the default. With `CLICI_RENDERER_KEYS=1`, the
native UI observes input context every 100 ms while active (one second while
unfocused), without forcing a redraw. Native events also publish updates.
Each carries a monotonic
observation time and a 750 ms expiry. A cached, focused pane qualification admits
exact Alt+Shift+Comma (previous) and Alt+Shift+Period (next) presses before Windows
Terminal clears selection or snaps to live output. Every admitted press must
then receive a native confirmation made **after** the press, resolve fresh painted
anchors, and pass scroll/readback checks.
Passive renewal does not extend admitted work. A newer native confirmation made
after the press may replace the admission lease while that press is still live.
The work remains bounded by the confirming lease and one second from the original
press; expired or invalidated presses cannot be revived.

On a US keyboard these are Alt+< and Alt+>; hold Alt and Shift, then press the
comma or period key. Both modifier orders work. Releasing either modifier first
keeps an accepted punctuation press consumed until key-up, while its pane context
is still valid. Ctrl+Up/Down now pass through. Configuration key names are
`Alt+Shift+Comma` and `Alt+Shift+Period`, not literal shifted glyphs.
Windows may also use [Alt+Shift to switch keyboard layouts](https://support.microsoft.com/en-us/office/languages/switch-between-languages-using-the-language-bar).
That system setting is unchanged; multiple-layout and IME use still need qualification.

The hook thread performs no COM calls, pipe I/O, scrolling, clipboard access, or
key replay. It uses the existing bounded queue and observes key release/repeat,
foreground/focus changes, pointer input, and other keys. Native modal draws
withdraw eligibility. Pane, geometry, native-session, prompt-list and input-scope
changes require requalification. Expired, ambiguous or unsupported contexts pass
through; a canceled press is never replayed. A stalled helper loses its lease and
the background supervisor stops it after its one-second pipe timeout. Startup,
pipe writes, reply waits, and process teardown never run on the native input
thread. One active observation and one replaceable pending observation bound
work. A replacement starts unqualified, after backoff from one second to a
30-second cap. Ten replacements exhaust a latched budget; restart Codex to reset
it. Killing a helper alone does not disable its supervisor. The installer disable
command affects future shells; close or relaunch an existing session bridge-off
to stop that session's navigation.

Ordinary typing revokes input eligibility without discarding the pane/layout cache
in either Codex v2 or Claude's legacy frame transport.
Renewal checks an existing painted witness and current focus/geometry off the
hook thread. Discovery is throttled for both transports and never scans a known
background pane. These protections must not depend on the optional v2 admission
mapping: legacy helpers otherwise rescan the foreground terminal on every
heartbeat or keypress, including when it belongs to another agent.
Full mapping narrows candidate blank-row blocks using bounded text, then checks
all six colors in UIA line coordinates. It does not infer coordinates from
CRLF or Unicode character counts. Scrolling still uses a fresh map and final
document, focus, selection, and readback checks.

`CLICI_NATIVE_KEY_PROBE` no longer intercepts native Ctrl+Up/Down: an asynchronous
bridge cannot supply its former synchronous handled result. Production
Alt+Shift+Comma/Period still use the pre-Terminal adapter.
`CLICI_PROOF_INJECTED_KEYS=1` accepts only
the probe's marked synthetic keys and only in proof-window mode; live mode always
rejects injected keys.

The original Ctrl+Up/Down native single-pane fixture verified duplicate prompt
identities, previous/next, return-to-live, an unsent draft and a nonempty selection.
The replacement Alt+Shift punctuation defaults passed the automated real-hook
sequence at 112, 1,000 and 3,916 rows and the actual isolated Codex draft/selection
check. See the [latency repair evidence](../../../../docs/prompt-navigation-latency-plan.md#implementation-evidence-2026-10-21)
for timings and remaining qualification.
Marked injected keys
exercise the real Windows hook; this is not physical-keyboard qualification.
The full modal/IME, split-pane, exit, redraw, eviction and upgrade matrix remains open.
Clipboard formats/selection-copy whitespace, physical keyboards, IME, and Windows
silently removing a low-level hook still need broader manual qualification.
Claude uses the same adapter through the bundled plugin; its physical keyboard
qualification is still open, see the
[Claude qualification report](../../../../docs/claude-renderer-qualification.md). See the
[proof report](../../../../docs/prompt-navigation-proof-report.md) for measured
results and the [contract](../../../../docs/prompt-navigation-native-contract.md)
for the remaining feature gates.

For the repeatable hook regression, start `PromptNavigation.Probe.exe
anchor-fixture <directory>` in a portable Terminal titled `CLICI-PROOF-input`,
then run `python test_live_input.py --probe <probe.exe> --fixture-directory
<directory>`. Keep that disposable window foreground. It includes a soft-wrapped
prefix, checks prompt IDs, selection, both modifier orders and early modifier
release, and verifies that eligible punctuation is consumed while ineligible
punctuation reaches the synthetic fixture. Only that fixture records received
test keys in `<directory>/keys.jsonl`. Use a fresh fixture directory for each run.
Results are saved under `<directory>/evidence/input-live.json`. Add `--frames`
to drive the same sequence through `renderer-bridge-frames`, the file transport
the Claude Code plugin uses, with lowercase nonce bindings; its results go to
`input-live-frames.json`. A background process cannot take the foreground, so
the fixture window must already be foreground when the script starts.

Add `--native-admission --navigation-repetitions 10` for the Codex v2 shared-word
path, forty navigation attempts, and a 100-character zero-rescan check. Use
`anchor-fixture <directory> 328` for approximately 1,000 rows or `1300` for
approximately 3,916 rows. Start with empty scrollback; earlier fixture output
counts toward the capture. The helper scans at most the most recent 4,096
physical rows, so older output does not disable recent prompt navigation. Use
`1800 dense` and a test Terminal history size above 5,500 to reproduce the
over-limit case; only the two newest prompts fit in the capture. A partial
anchor at the cutoff still refuses rather than guessing a boundary. The harness
waits up to five minutes for stable foreground input and sends keys only to the attested proof window.

Add `dense` after the fixture row count to include indented replies and blank
rows, for example `anchor-fixture <directory> 328 dense`. Create the directory
first. This reproduces the October 24 scan timeout that the sparse synthetic
history missed. `renderer-inspect <hwnd> <directory>/anchors.json` checks mapping
without focus or injected input; the original scanner exceeds its deadline on
this 1,000-row fixture. The range scanner fixes that mapping regression and keeps
UIA line coordinates across wrapping. A mapping result alone does not qualify
keyboard handling: run the dense fixture through both `--native-admission` and
`--frames`, including the typing and selection checks.

For an isolated actual Codex session with a known synthetic unsent draft, run
`PromptNavigation.Probe.exe typing-latency <hwnd> CLICI_FIXTURE_UNSENT_DRAFT 100`.
This appends 100 lowercase `a` characters without submitting the prompt and
records SendInput-to-visible-UIA-text timings, including polling overhead.
It requires the portable proof window, active pane and exact draft marker;
it is not a physical-keyboard or IME measurement.
