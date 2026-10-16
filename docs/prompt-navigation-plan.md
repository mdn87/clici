# Clici prompt navigation implementation plan

- **Status:** Proposed; terminal targeting and message recognition require a prototype.
- **Date:** 2026-10-15
- **Outcome:** Jump between user messages in the active Claude Code or Codex
  terminal session with configurable shortcuts, initially Ctrl+Up and Ctrl+Down.
- **Initial target:** Windows Terminal on Windows 11, using the normal scrollback
  buffer with locally running Claude Code or Codex.

Add an optional navigation feature to Clici that moves the existing terminal
viewport to a user message. Preserve the current classic terminal workflow.
Implement this through a Windows Terminal adapter if a focused prototype proves
that it can identify message boundaries and scroll to them reliably.

This is a proposed expansion of Clici's clipboard-only product scope. It does
not change the existing v0.1 proof plan. Implementation must update the product
scope and support claims alongside the new behavior.

## Prerequisites and open decisions

- **v0.1 functional baseline.** The 19 functional cases in
  `docs/v0.1-proof-ledger.md` had not been run as of the 2026-08-29 handoff.
  Phase 4 requires copying and Ctrl+Alt+J to keep working, which needs a
  recorded baseline to compare against. Run the ledger before Phase 4.
- **Product placement.** `CONTRIBUTING.md` defines margin cleanup and line
  rejoining as the whole product, and the README promises no global keyboard
  hook. The reusable parts are the tray, JSON configuration, and hotkey
  plumbing. A component that reads terminal text through UI Automation, and
  possibly hooks the keyboard, can look keylogger-shaped to Defender and EDR
  heuristics. Decide before Phase 3 whether navigation ships inside Clici or as
  a separate executable that shares `clici.Core` and lifecycle code. The Phase 3
  layout below assumes the in-process option and changes if the separate
  executable is chosen.

## Existing work and starting point

| Precedent | What to reuse or learn |
| --- | --- |
| [Claude fullscreen navigation](https://code.claude.com/docs/en/fullscreen#search-and-review-the-conversation) | Ctrl+O followed by `{` or `}` already navigates prompts in the fullscreen transcript. |
| [Claude prompt-jump mod](https://github.com/ruanss4/prompt-jump) | Tracks user-message IDs and calls the renderer's scroll API. Its previous/next controls and one waypoint per prompt are useful interaction references. Fullscreen is required. |
| [Codex Ctrl+Up/Down implementation](https://github.com/openai/codex/issues/47300) | A community fork implements these keys through backtrack browsing. That interface retains Enter-to-rewind and is not the behavior Clici should invoke. |
| [Windows Terminal shell integration](https://learn.microsoft.com/en-us/windows/terminal/tutorials/shell-integration) | Native `scrollToMark` actions provide the desired motion once marks exist. Shell integration alone does not identify each conversation turn. |
| [UI Automation ScrollIntoView](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.scrollintoview?view=windowsdesktop-10.0) | Supports aligning a text range to the viewport top. Windows Terminal implements this in [UiaTextRangeBase.cpp](https://github.com/microsoft/terminal/blob/main/src/types/UiaTextRangeBase.cpp). Live feasibility remains to be tested. |

The research baseline found Windows Terminal 1.24.12741.0 running, with npm
packages Claude Code 2.1.296 and Codex 0.162.1 installed. Claude's user settings
select `tui=default` and set `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`. Record
actual executable versions and renderer modes again when running the prototype.

Clici already has a tray lifecycle, JSON configuration, a clipboard listener,
and a global join-lines hotkey. Its foreground provider returns a process name,
which cannot distinguish terminal panes. `HotkeyParser` currently accepts
letters, digits, and function keys; arrow keys need explicit support.

## User behavior and scope

| Requirement | Expected behavior |
| --- | --- |
| NAV-001 Previous prompt | Ctrl+Up brings the owning user prompt to the top if its beginning is above the viewport. If the last jump targeted that prompt, another press goes to the preceding user prompt. From live output, the first press reaches the latest user prompt. |
| NAV-002 Next prompt | Ctrl+Down advances to the next user prompt. Advancing past the newest prompt returns to live output. At live output it does nothing. Phase 1 must name the mechanism that returns the viewport to live output. |
| NAV-003 Reading only | Navigation preserves the input draft, selection, clipboard, and conversation. It never submits text, recalls input history, rewinds, forks, or interrupts a running agent. |
| NAV-004 Correct pane | Only the active eligible terminal pane moves. Unsupported apps, ordinary shells, and other panes keep their normal keyboard behavior. |
| NAV-005 Stable targets | Repeated prompts remain separate waypoints. Resizing, reflow, buffer eviction, session changes, and redraws invalidate stale targets before scrolling. |
| NAV-006 Optional feature | Navigation is disabled by default and has its own enable switch and configurable shortcuts. Turning it off removes its input interception. Existing clipboard controls retain their meaning. |
| NAV-007 Clear limits | When the pane, renderer, or prompt boundary cannot be established, leave the viewport unchanged and expose a concise reason in the tray status. |

NAV-001 cannot infer "already at this prompt" from the viewport alone. Near the
buffer end a prompt may never reach the top row, so a viewport test would keep
re-selecting the same prompt. Keep a navigation cursor holding the last target,
its pane identity, and its snapshot generation. Fall back to viewport position
only after the cursor is invalidated by NAV-005 events or manual scrolling.

The initial feature covers retained scrollback in tested Windows Terminal
configurations. Fullscreen/alternate-screen agents, VS Code/Cursor terminals,
other terminal emulators, SSH, and WSL require separate qualification.
Elevated (administrator) Windows Terminal windows are out of scope: UIPI blocks
a non-elevated process from UI Automation access to them and from hooking their
input. Older
messages that have left the terminal buffer are outside this feature's scope.
Do not change an agent's renderer automatically.

## Phase 1 Prove terminal access and shortcut handling

Build a small development probe under `tools/proof/prompt-navigation/`. Use
synthetic conversations in disposable test panes. Keep the probe independent
of Clici's clipboard pipeline. Use the COM UI Automation client
(`IUIAutomation`), the same client the shipped adapter would use. The managed
`System.Windows.Automation` wrapper linked above is the legacy client, lacks
`TextPattern2`, and behaves differently, so its evidence would not transfer.

0. Test Windows Terminal's native marks before building the UI Automation
   probe. In a scratch profile, enable `experimental.autoMarkPrompts` (marks
   the row where Enter is pressed), bind `scrollToMark` with `previous` and
   `next` to Ctrl+Up and Ctrl+Down, and run a Claude session and a Codex
   session. Record whether marks land on the rendered user message and
   survive renderer redraws, resize, and streaming output. If they do, Windows
   Terminal already provides pane targeting, scrolling, and shortcut isolation
   (its bindings never leave Windows Terminal), and steps 1, 3, and 5 and most
   of Phase 3 may be unnecessary. The known cost is that Windows Terminal
   bindings apply to every profile, so ordinary shells also lose Ctrl+Up and
   Ctrl+Down; that conflicts with NAV-004 and needs an explicit decision. If
   the marks do not hold, continue with step 1.
1. Resolve the foreground Windows Terminal window and its focused text control.
   Establish pane identity from the window and UI Automation element, rather
   than treating every pane belonging to `WindowsTerminal.exe` as one session.
2. Read visible ranges and bounded retained text through UI Automation. Inspect
   available text attributes and layout evidence for Claude and Codex user
   messages. Include the current composer and assistant code that imitates
   prompt delimiters in the fixtures.
3. Find a known synthetic message and call `ScrollIntoView(true)`. Verify the
   actual viewport, including alignment near the buffer end where a full
   top-alignment may be impossible. Repeat in split panes and multiple tabs.
4. Exercise resize/reflow, new streaming output, manual scrolling, clear/resume,
   repeated message text, and focus changes. Determine which text ranges can
   be reused and which must be reacquired.
5. Prove how shortcuts can be scoped to an eligible pane. `RegisterHotKey` is
   system-wide: checking focus after receiving `WM_HOTKEY` does not restore a
   chord already consumed in another application. Test focus-scoped registration
   and its transitions before choosing it. Switching panes or tabs inside one
   Windows Terminal window does not change the foreground window, so
   focus-scoped registration needs UI Automation focus-changed events, not
   only foreground-window events.

If registration cannot preserve normal input outside eligible panes, evaluate
a narrowly filtered keyboard hook. Its callback may only consult cached
eligibility, queue one of the two navigation actions, or pass input through;
it must never call UI Automation or retain typing. Windows silently removes a
low-level hook whose callback exceeds `LowLevelHooksTimeout`, so the hook needs
removal detection, reinstallation, and a test that forces the condition. This
would revise Clici's current no-global-keyboard-hook claim and must be
documented explicitly.
The prototype must resolve this choice before integration. Do not silently
substitute different keys or ship unconditional Ctrl+Up/Down registration.

Save environment details, synthetic fixtures, timings, and viewport captures
under `artifacts/prompt-navigation/<run>/`. Prefer remotedesk/lugos.vision for
captures when available. Summarize results in
`docs/prompt-navigation-proof-report.md`.

**Exit condition:** Both agents have evidence for correct pane targeting,
recognizable user messages, reliable scrolling, and shortcut isolation. If a
renderer lacks sufficient evidence, identify the missing capability and narrow
the supported scope. If neither renderer is viable and step 0's native marks
also failed, record an alternative design using app-native anchors or
deliberately emitted terminal marks before building the integrated feature.
A hook firing at submission time alone does not prove that a mark lands on the
rendered user-message line.

## Phase 2 Implement message recognition and navigation decisions

Add pure navigation decisions under `src/clici.Core/Navigation/`, with fixtures
and tests in `tests/clici.Core.Tests/`.

- Define a bounded terminal snapshot, ordered prompt candidates, viewport
  position, navigation direction, and explicit success/refusal results.
- Implement separate Claude and Codex recognizers using the evidence established
  in Phase 1. A leading `>` or similar glyph alone is insufficient: shell
  prompts, quoted replies, and code examples can contain it too.
- Exclude the editable composer, tool output, permission dialogs, and unsupported
  renderer layouts. Only rendered user messages become waypoints.
- Select previous/next targets by position in the active pane's snapshot. Do not
  identify a repeated prompt by searching for its text and taking the first match.
- Keep pane identity and snapshot generation with every target. Reacquire ranges
  after invalidation and confirm the pane is still active before acting.

Start with on-demand bounded snapshots. Optimize caching only after measurements
justify it. Avoid scanning private session-history directories as a substitute
for establishing the active pane. If transcript metadata becomes necessary,
first define an explicit pane-to-session binding and its lifecycle.

**Exit condition:** Tests cover previous/next semantics, duplicate prompts,
multiline and Unicode content, false prompt examples, composer exclusion,
buffer boundaries, and stale-target refusal using realistic synthetic fixtures.

## Phase 3 Integrate an optional Windows navigation service

| Area | Proposed responsibility |
| --- | --- |
| `src/clici.App/Navigation/` | Interfaces and Windows Terminal adapter for pane discovery, snapshots, range resolution, and scrolling. |
| `src/clici.App/Input/` | Arrow-key parsing and the shortcut mechanism qualified in Phase 1. Keep it independent of clipboard notifications. |
| `src/clici.App/TrayApplicationContext.cs` | Own the navigation service lifecycle, enable control, and concise status. Configuration changes currently require a restart; state whether the tray toggle and shortcut fields apply at runtime or after restart. |
| `src/clici.Core/Configuration/CliciConfiguration.cs` | Add `promptNavigationEnabled` defaulting to `false`, `previousPromptHotkey` defaulting to `Ctrl+Up`, and `nextPromptHotkey` defaulting to `Ctrl+Down`. |
| Configuration store and tests | Increment the schema version when fields ship; preserve old-file defaults and malformed-file behavior. Reject conflicting navigation chords without disabling clipboard cleanup. |

Keep UI Automation off the tray/message-loop thread. Use one bounded worker
queue and coalesce held-key repeats rather than accumulating scroll requests.
Apply a request deadline and recheck pane identity before moving the viewport.
A timeout does not cancel an in-flight COM call: do not spawn replacement
workers indefinitely when a provider hangs. Suspend the adapter, report the
failure, and keep the tray and clipboard service responsive.

Keep terminal text transient in memory. Diagnostics may record result codes,
counts, timings, and supported-version information, but no prompt or terminal
contents. Navigation must not use the clipboard as a transport or send
synthetic conversation commands.

**Exit condition:** Clici can toggle navigation, handle configuration and input
conflicts, survive adapter failures, and shut down without leaving shortcuts
registered. Existing clipboard tests remain green.

## Phase 4 Validate the complete user interaction

Run the following matrix for both qualified agent renderers:

| Case | Evidence required |
| --- | --- |
| Long conversation | Repeated Up/Down presses land on the expected prompts in order and return to live output. |
| Ambiguous text | Assistant code, shell syntax, echoed prompts, repeated prompts, and dialog text are not mistaken for a user-message boundary. |
| Layout changes | Wrapped text, Unicode, window resizing, and font/zoom changes preserve correct targeting or produce a safe refusal. |
| Active generation | The agent continues working; navigation preserves draft text and remains stable while output arrives. |
| Multiple panes | Tab/split switching and rapid focus changes move only the active eligible pane. |
| Other input contexts | Editors, browsers, ordinary shells, unsupported/fullscreen agents, elevated Windows Terminal windows, and disabled navigation retain their normal shortcuts. |
| History changes | Buffer eviction, clear, resume, and compaction/redraw behavior do not reuse stale anchors. |
| Failure and coexistence | Shortcut conflicts, provider timeouts, restart, disable, and exit leave Clici responsive; copying and Ctrl+Alt+J still work. |

Use fakes for coordinator ordering and failures, pure fixtures for recognition,
and actual Windows Terminal sessions for UI Automation and keyboard behavior.
Screenshots must demonstrate the requested prompt and the viewport after the
jump; a successful API return alone is insufficient.

Measure cold and repeated navigation on the declared buffer limit. Aim for
repeated jumps within 250 ms and fail a stalled request within one second,
without blocking clipboard processing. Record actual limits and timings in the
proof report rather than claiming unlimited scrollback support.

After meaningful implementation batches, run targeted tests. Before landing the
finished feature, inspect the full scoped diff and run:

```powershell
dotnet restore clici.sln
dotnet build clici.sln --configuration Release --no-restore
dotnet test clici.sln --configuration Release --no-build
dotnet format whitespace clici.sln --verify-no-changes
git diff --check
```

## Phase 5 Document and deliver

Update the README, CONTRIBUTING scope, configuration examples, navigation
requirements, changelog, and release checklist to match the implemented support
boundary and shortcut mechanism. Link the proof report and explain renderer
limitations and key conflicts in the operator instructions.

Preserve unrelated working-tree changes. Land only this feature's validated
changes through the normal Git/CI workflow and build the existing Windows
installer. Retain the previous installer for recovery. Installing the new build
or changing user-level settings is a separate execution action; this plan does
not perform either.

The feature is complete when both declared agent configurations pass the live
matrix, unsupported contexts retain normal input, clipboard behavior still
passes its existing checks, and the published support claims match the evidence.

**First implementation batch:** the Phase 1 step 0 native-marks test, then the
probe and proof report if the marks do not hold. Settle terminal access, prompt
recognition, shortcut scoping, and product placement before adding a permanent
navigation service to Clici.
