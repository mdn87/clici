# Codex renderer bridge experiment

This is an opt-in source patch for Codex **0.162.1**, commit
`092d3acd6bec3e3a14bdc7e7a2810ab628ab759d` (`rust-v0.162.1`). It carries native
user-message IDs into the classic scrollback renderer and sends metadata to a
private child of that process. It does not install a replacement CLI, enable
Clici navigation, or change ordinary `codex` / `claude` launch commands.

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
- The observed pairs must match a suffix of the native prompt list. An evicted
  prefix becomes a capture boundary. Missing interior/newest prompts, repeated
  IDs/nonces, partial paint, duplicate paint, and invalid order refuse navigation.
- Metadata uses private stdin/stdout pipes, contains no prompt text, and carries
  process/session identity, sequence, epoch and a monotonic deadline. One I/O
  worker bounds pipe waits; failure kills the owned helper and disables it.

The companion helper operates only in explicitly named portable
`CLICI-PROOF-*` Windows Terminal windows. Its bound is 4,096 prompts/rows,
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

## Input limitation and remaining qualification

Passive publication is the default. **It does not intercept Ctrl+Up/Down.**
`CLICI_NATIVE_KEY_PROBE=1` separately enables an experimental native input path,
gated to the normal composer. It is a diagnostic, not a supported shortcut.
Windows Terminal can clear selection and snap to live output *before* delivering
the key to Codex. Thus the native handler cannot satisfy NAV-003 or reliably
retain the navigation cursor under the ordinary Terminal defaults.

A usable shortcut needs interception before Terminal handles the key, plus a
fresh native eligibility handshake and verified pane binding. This patch's
passive input-context field is **not an eligibility lease**. No global hook is
installed, no hook timeout recovery is implemented, and `NavigationInputQueue`
is not wired to an OS adapter. Manual scrolling away and back between observations
also needs event-based invalidation. Large histories fail closed at the bounds.

The helper's synthetic fixture and native writer tests do not qualify the full
modal/IME, nonempty draft, split-pane, exit, redraw, eviction and upgrade matrix.
Clipboard formats/selection-copy whitespace need separate real copy checks.
Claude still requires a renderer/input API extension. See the
[proof report](../../../../docs/prompt-navigation-proof-report.md) for measured
results and the [contract](../../../../docs/prompt-navigation-native-contract.md)
for the remaining feature gates.
