# Prompt navigation proof report

**2026-10-16 — development prototype, not a supported navigation feature.**

COM UI Automation can read and scroll the tested Windows Terminal panes. Native
mark bindings fail the requested navigation/isolation behavior. A follow-up batch
adds native identity validation, bounded key admission and a tested Claude ID
collector. Current-pane ownership and the classic-mode renderer/input bridge
remain unproved, so no navigation service, shortcut registration, or keyboard
hook has been added to Clici.

The operator chose to **keep existing `claude` and `codex` launch commands and
investigate agent-native integration**, rather than introduce a Clici launcher.
Classic retained scrollback remains the target; fullscreen has not been accepted
as a substitute.

## Environment and reproducibility

Local evidence: `artifacts/prompt-navigation/20261015-phase1/`, based on
`498f1381d4c4cc573eff49e4d4ecb4b94622e03d`. Captures contain disposable synthetic
conversations. They are Git-ignored; the probe and
[runbook](../tools/proof/prompt-navigation/README.md) are versioned.

| Component | Tested configuration |
| --- | --- |
| Windows Terminal | Official portable x64 1.24.12741.0; isolated `.portable` settings store; 1,000 history rows; Cascadia Mono 11; initial 105 columns / 38 rows. |
| Native marks | `autoMarkPrompts=true`; Ctrl+Up/Down bound to previous/next `scrollToMark`; clean fixture and fixture inside an OSC 133 command. |
| UIA client | COM `IUIAutomation`, MTA worker, NuGet `Interop.UIAutomationClient` 10.19041.0; .NET SDK 10.0.113. |
| Claude Code | 2.1.296; `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`; `--safe-mode --tools '' --permission-mode plan --strict-mcp-config --no-chrome`. |
| Codex | 0.162.1; `--no-alt-screen --no-daemon --disable hooks --sandbox read-only --ask-for-approval never -c 'notify=[]'`. |
| Capture | Default tail 32,768 characters; separate 512-character cutoff check; tool maximum 262,144. |

The portable ZIP SHA-256 matched the official release asset digest:
`7aaa8321064c45d5af79205db13deb87f146f644bd8abdfae29f0155d7ce744e`.
The installed Windows Terminal settings were not edited. Screenshots were taken
through the local remotedesk capture implementation and cropped to test content.
All disposable terminal processes were closed after the experiments.

An unmodified clipboard baseline is retained at `baseline/app/`, informational
version `0.1.0+498f1381d4c4cc573eff49e4d4ecb4b94622e03d`. Its 119 existing unit
tests passed. Actual Ctrl+Alt+J delivery, disabled/paused/conflict behavior, and
the repaired v0.1 manual ledger remain prerequisites for an integrated build.
This batch does not claim those manual checks passed.

## Observations

| Experiment | Actual result | Evidence under `evidence/` |
| --- | --- | --- |
| Native Up from live, no OSC command mark | With submitted prompt 003 visible and composer 004 at the bottom, first Up targeted 002. It skipped the newest submitted prompt. | `clean-after-three-turns.json`, `clean-up-1.json` |
| Native keys in an ordinary fixture process | Enter reached the process; Ctrl+Up/Down did not. Terminal consumed those chords regardless of agent eligibility. | `clean-input-events.jsonl`, `clean-up-*.json`, `clean-down-1.json` |
| Existing OSC command | Up targeted the shell command that launched the synthetic agent, rather than its individual user turns. | `osc-native-up.json` |
| Provider selection | `TermControl` exposes TextPattern; TextPattern2 was false. Filtering only on TextPattern also selected a tab-header TextBlock, so the probe now filters the tested class. | `clean-initial.json`, `claude-pane.json`, `codex-start.json` |
| Synthetic COM jump | Prompt 002 reached the top. A prompt near the buffer end remained below the top when the viewport could not scroll farther. | `osc-com-jump-002.json`, `clean-com-jump-003.json` |
| Return to live | A fresh document range collapsed to its end and scrolled into view followed the next 35 rows, appended through a control file without keyboard input. | `osc-com-live.json`, `osc-com-live-after-passive-output.json`, matching PNG |
| Real renderer text | Claude and Codex exposed the submitted multiline prompt, answer, deliberately prompt-like answer text, and composer. Claude's live pane accepted COM jump and return-to-bottom calls. | `claude-start.json`, `codex-start.json`, `claude-com-jump.json`, `claude-com-live.json` |
| Message attributes | In both agents, user text and prompt-like assistant text had identical foreground/background, weight 400, `readOnly=false`, and unsupported/mixed style. These attributes do not establish message roles. | `claude-*-attributes.json`, `codex-*-attributes-confirmed.json` |
| Exit without focus change | Claude exited normally with two Ctrl+C presses. Codex was deliberately terminated after verifying its fixture PID and command. Both returned to a waiting PowerShell prompt with the same runtime ID/name and retained transcript. Neither exit produced a UIA focus event. | `claude-after-exit.json`, `claude-exit-focus-events.json`, `codex-after-forced-exit.json`, `codex-forced-exit-focus-events.json` |
| Tabs and split panes | Focus events identified tab/pane changes. A two-pane snapshot selected the focused runtime ID; jumping in that pane left the other pane's visible text unchanged. | `tab-switch-focus-events.json`, `split-focus-events.json`, `split-before-jump.json`, `split-after-jump.json`, `split-focused-pane.png` |
| Resize/reflow | Resizing from 1,280 to 900 pixels changed captured layout while retaining the pane runtime ID. Pane identity is not a layout epoch. | `split-before-resize.json`, `split-after-resize.json` |
| Capture limit | A 512-character tail correctly reported that it did not reach the document start. Visible text is independently truncated by that same limit. | `osc-bounded-snapshot.json` |
| Lost foreground | A pane could report keyboard focus while another app was foreground. Scroll/input guards refused. With two panes and no unique focused control, selection tests also refused; preservation was not demonstrated. | `codex-com-jump.json`, `codex-cancel-1.json`, `split-select.json` |

The synthetic read/jump calls measured roughly 9–15 ms after discovery. These
numbers exclude cold worker startup and do not establish the 250 ms complete
navigation target or the one-second production failure bound.

`FindText` enumeration also returned an unusable COM range after the final
confirmed match in this provider. The diagnostic enumerator now checks bounded
text before searching again; jumps verify returned text before scrolling.
This is not an occurrence-resolution implementation for repeated real prompts.

## Requirements and implementation boundary

| Requirement | Status after this batch |
| --- | --- |
| NAV-001 / NAV-002 | Pure previous/next decisions implemented. Synthetic jumps and passive live follow demonstrated. Native mark behavior rejected. Full real-agent interaction unqualified. |
| NAV-003 | No clipboard access or conversation commands in navigation code. Preservation of a real draft and non-empty selection remains unverified. Test-key injection exists only in the development probe. |
| NAV-004 | Focused-pane discovery and split targeting demonstrated. Focus events alone do not establish current agent eligibility. No interception mechanism selected. |
| NAV-005 | Core rejects stale pane/session/layout/context identities and distinguishes capture limits. Adapter invalidation on eviction/redraw/manual scroll remains unimplemented. |
| NAV-006 / NAV-007 | No shipped feature or settings yet. Probe emits explicit refusal results. Runtime disable, tray status, and conflict handling remain future integration work. |

`src/clici.Core/Navigation/PromptNavigation.cs` accepts complete, positively
recognized prompt occurrences and returns Jump, ReturnToLive, NoOp, Refused,
CaptureLimit, or PassThrough. A verified occurrence cursor survives observation
refreshes and append-only output, including a newly appended later prompt.
True history ends are distinct from artificial capture ends. Duplicate text
does not collapse distinct positions.

The caller must supply immutable observations, authoritative eligibility and
coverage, and positions valid within a layout epoch. It must recheck before
scrolling and create the cursor only after verifying the result. No recognizer,
asynchronous coordinator, held-key policy, or cursor-notification arbitration
is implied by this pure decision API.

Validation: the first 23 navigation cases failed on the initial stub, then
passed after implementation; an additional append case brings navigation
coverage to 24. The full solution passed **143 tests** (90 Core, 53 App), and
built with zero warnings/errors after the disposable fixtures were closed.
The probe is part of the solution, so normal CI builds and formats it. The
PowerShell launch/timeout helpers parsed successfully; the timeout wrapper's
`windows` command was smoke-tested. The new launcher itself has not been run
end to end; the equivalent isolated setup used in this experiment was manual.

## Agent-native investigation and next batch

1. **Claude function hooks are the promising metadata surface.** The existing
   [prompt-jump implementation](https://github.com/ruanss4/prompt-jump/blob/main/hooks/register.tsx)
   receives stored user-message UUIDs through `session.append` and sees
   `UserMessage` render events. Its [documented scroll path](https://github.com/ruanss4/prompt-jump)
   uses `$.ui.scroll({to:{requestId:uuid}})` and requires fullscreen. A local,
   empty-plugin capability test passed on 2.1.296 without installing a plugin
   or making a model request. This proves the test runtime is available, not
   classic-mode navigation or keyboard interception.
2. **Standard hooks alone do not place marks.** Claude's
   [hook reference](https://code.claude.com/docs/en/hooks#emit-terminal-notifications)
   restricts `terminalSequence` to selected notification/title OSCs and BEL;
   OSC 133 is outside that allowlist. `MessageDisplay` transforms assistant
   output only. Codex's [hooks](https://learn.chatgpt.com/docs/hooks) expose
   session/turn IDs and submitted prompt text, but the documented payload does
   not establish a rendered row or the currently focused Terminal pane.
3. **Codex's existing fork is a behavioral reference, not a safe substitution.**
   [Issue 47300](https://github.com/openai/codex/issues/47300) remains open.
   Its linked [arrow-navigation implementation](https://github.com/ralyodio/codex-dx/blob/tui-arrow-prompt-navigation/codex-rs/tui/src/app_backtrack/arrow_navigation.rs)
   enters the existing backtrack flow, whose Enter action rewinds. The pinned
   [0.162.1 source](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/tui/src/app_backtrack.rs)
   confirms that contract. NAV-003 needs a separate read-only path.

Next, qualify a native integration that handles navigation inside the active
agent and supplies authoritative message identities to a read-only scroll path.
For Claude, establish whether function hooks can receive the requested keys in
classic mode and provide enough renderer/pane evidence for a COM bridge.
For Codex, locate a distinct read-only navigation path in the renderer rather
than remapping rewind. Prove those capabilities before implementing either
bridge. If a renderer change is required, specify the exact change and its
maintenance cost before proposing installation or replacing either CLI.

No launcher, plugin installation, vendor binary patch, global setting, or
renderer change was made. Product placement before Phase 3 remains open;
the no-wrapper decision is settled. The feature's Phase 1 exit condition and
full Phase 2 recognition tests remain unmet.

## Follow-up: prompt IDs and key admission

Evidence: `artifacts/prompt-navigation/20261016-native/`, based on `153dc4a`.
The [native bridge contract](prompt-navigation-native-contract.md) describes the
implemented API and what remains outside its guarantees.

- Native agent/session IDs now namespace each snapshot. Each rendered occurrence
  requires an immutable message ID in addition to position/context evidence.
  Missing/duplicate IDs refuse navigation; identical text or a reused position
  cannot revalidate a different message.
- `NavigationInputQueue` starts disabled, bounds pending work, coalesces held
  repeats to one action per press, preserves distinct presses, and rejects stale
  work on focus/session/layout/eligibility changes, manual scroll, disable and
  deadlines. It handles admission only; it installs no OS input mechanism.
- `TryParseNavigation` accepts modified arrow keys without widening the existing
  join-hotkey grammar. No configuration schema or clipboard behavior changed.
- The [Claude identity probe](../tools/proof/prompt-navigation/claude-identity/README.md)
  collects main composer append UUIDs and corresponding render-request IDs,
  discards old generations on clear/resume/compaction/exit, and stores no prompt
  text. Render-only candidates are not proof of main-loop membership or a painted
  terminal row. Reports always say `navigationEligible: false`.

Claude's embedded 2.1.296 declaration confirms that `ui.press` and `ui.input`
are plugin-control events. `ClientSurface.onKey` requires plugin-region focus;
Button actions reuse existing engine bindings. The classic main screen does not
supply `onScreen` transcript coordinates. This establishes an API limitation,
not a successful native Ctrl+Up/Down path. The headless loader did not generate
declarations, so the run inspected its embedded API declaration read-only and
type-checked the probe against it; no vendor code is committed.

Codex's pinned 0.162.1 protocol provides `UserMessageItem.id`. Its committed-user
display path handles steering/replay and constructs history cells without a
message-ID argument. A renderer extension must preserve that ID and expose a
read-only action; remapping the existing backtrack flow remains unsuitable.

Validation: **184 .NET tests** (121 Core, 63 App), **8 Claude runtime tests**,
and **6 independent Node ledger tests** pass. TypeScript checking and native
plugin validation pass. The actual headless Claude CLI loaded the plugin and
ran `/clici-prompt-ids` without a model request; it correctly reported inactive,
empty metadata in that noninteractive run. CI includes the Node tests; the
Claude runtime/type checks require the installed pinned build and run locally.

These checks do not establish real interactive render anchors, raw keyboard
delivery/isolation, live draft/selection preservation, hook-timeout recovery or
the Phase 1 exit condition. Those limits remain explicit rather than enabling
navigation on a metadata-only signal.

## Follow-up: Codex renderer bridge

Evidence: `artifacts/prompt-navigation/20261016-renderer/`, based on Clici
`86fab8d` and Codex tag `rust-v0.162.1`, commit
`092d3acd6bec3e3a14bdc7e7a2810ab628ab759d`. The
[reproducible source patch](../tools/proof/prompt-navigation/codex-renderer/README.md)
is opt-in and operates with the COM helper in portable proof windows only.
Installed CLI binaries and the user's normal launch commands were not replaced.

### What was observed

- The actual patched Codex TUI ran in classic scrollback mode against an isolated
  loopback response fixture, with a separate Codex home. No account credentials
  or external model request were needed. Its private child received the native
  user-item ID and nonce, and COM independently found both painted blank-row
  anchors around a wrapped Unicode prompt. The native prompt covered rows 8–11
  at the original window size; after resizing to 650 and 1,100 pixels wide,
  fresh observations found it at 8–12 and 7–9 with the same native ID and nonce.
  See `evidence/native-renderer-color-inspection.json` and `native-resize.json`.
- In a separate synthetic fixture, three identical Unicode prompts retained
  separate IDs and anchors. Four private-pipe requests verified latest →
  previous → latest → live. The four RPCs took approximately 313, 356, 66 and
  75 ms. Selection readback verified the existing empty selection; preservation
  of a nonempty selection was not established in this batch. See
  `evidence/anchor-live-batch.json` and `renderer-visible.png`.
- Sending Ctrl+Up to that fixture after scrolling up changed the viewport back
  to live before the fixture did anything with the key. This confirms the
  snap-on-input path in Terminal 1.24.12741.0. Its source also handles selection
  before native key delivery. Native Ctrl+Up/Down is therefore a separately
  gated diagnostic, not the supported shortcut implementation.
- A color-disabled native trial published metadata but had no painted anchors;
  the helper refused the map. The launcher had inherited `NO_COLOR=1` from the
  tool environment. The final native bridge explicitly stays off under
  `NO_COLOR`; the successful trial enabled color only in its disposable process.
- The native writer regression test first failed because wrapping removed the
  colored spaces. A narrow wrapping exception now preserves the six-cell blank
  anchors. The test uses the real history writer and VT100 backend at widths 24
  and 80, verifies RGB attributes, and compares the complete visible text grid.
  Explicit spaces differ from implicit blank padding for consumers that retain
  trailing whitespace, so this is not a claim of identical rich/plain clipboard
  serialization. No clipboard API is called by the bridge.

### Implementation and limits

Native item IDs are carried through committed messages, optimistic receipts,
steering and replay construction. Metadata crosses private pipes without prompt
text. Complete RGB anchor pairs are resolved against the native order; missing
interior/newest messages, duplicate IDs/nonces and ambiguous paint refuse.
An evicted prefix creates a capture boundary. Requests have bounded sizes and
monotonic deadlines; the native caller stops its owned helper after a pipe or
provider timeout. A possible scroll is never followed by key replay.

Passive publication is not a fresh input-ownership lease. No global hook or tray
feature is enabled. A pre-Terminal input adapter still needs fresh native
eligibility, verified pane ownership, event-based manual-scroll invalidation,
timeout recovery and the full draft/selection/modal/IME/split/exit matrix.
Manual scrolling away and back between observations is not detected. Large
histories fail closed at the documented bounds. Claude's API limitation remains.
The full Phase 1 exit condition is still unmet.

Validation: **197 .NET tests**, **11 standalone Rust protocol/pipe tests**, and
**6 Node ledger tests** pass. The native source builds with Rust 1.99/MSVC, and
the eight focused bridge/writer tests pass. Patch application was checked on a
clean checkout at the pinned commit and compared with the built source.
Across the selected native bridge, history-cell, submission, projection,
receipt and replay checks, **231 tests passed and two were ignored**. The last
four groups used the same compiled test executable directly while another
build held Cargo's shared output lock.
The broader native history-cell run exposed three existing release-version
snapshots expecting `0.0.0` while the tag reports `0.162.1`; those snapshots were
left unchanged and excluded from the subsequent focused run. They are not
reported as passing. Native warnings from the pinned upstream code remain.
