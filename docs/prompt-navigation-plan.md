# Clici prompt navigation implementation plan

- **Status:** The opt-in Codex renderer bridge includes pre-Terminal shortcut interception and fresh native confirmation. The original Ctrl+Up/Down single-pane checks passed. The replacement Alt+Shift punctuation shortcuts pass automated checks; their complete live sequence remains unqualified. Broader qualification, Claude integration and tray controls remain unfinished. Navigation is not globally enabled.
- **Date:** 2026-10-17
- **Outcome:** Jump between user messages in the active Claude Code or Codex
  terminal session with configurable shortcuts. The 2026-10-17 operator-selected
  defaults are Alt+Shift+Comma (previous) and Alt+Shift+Period (next), or Alt+<
  and Alt+> on a US keyboard. These replace the original Ctrl+Up/Down defaults.
- **Initial target:** Windows Terminal on Windows 11, using the normal scrollback
  buffer with locally running Claude Code or Codex.

Add an optional navigation feature to Clici that moves the existing terminal
viewport to a user message. Preserve the current classic terminal workflow.
Implement this through a Windows Terminal adapter if a focused prototype proves
that it can identify message boundaries and scroll to them reliably.

This is a proposed expansion of Clici's clipboard-only product scope. It does
not change the existing v0.1 proof plan. Implementation must update the product
scope and support claims alongside the new behavior.

The [Autowork/Roundtable review of 4667398](prompt-navigation-review-4667398.md)
records the evidence, corrections, and remaining experiments behind this revision.

## Prerequisites and open decisions

- **Renderer experiment (2026-10-16).** The pinned
  [Codex patch and private-pipe helper](../tools/proof/prompt-navigation/codex-renderer/README.md)
  carry native item IDs to independently observed colored blank-row anchors.
  Windows Terminal handles selection and snap-on-input before delivering a key
  to Codex, so a native shortcut alone cannot meet NAV-003. Keep passive metadata
  publication separate from the diagnostic key path. The implemented adapter
  intercepts before Terminal, obtains fresh native input eligibility, verifies its
  pane binding, and invalidates on pointer/focus input. Broader manual-scroll
  and hook-recovery qualification remain open.
  Do not enable navigation from the passive report's input-context flag.
- **Keep existing launch commands (operator decision, 2026-10-16).** Do not
  introduce `clici claude` / `clici codex` as a prerequisite. Investigate
  agent-native integration while retaining classic scrollback. The
  [first proof report](prompt-navigation-proof-report.md) records the native-mark
  failures, successful COM experiments, remaining gaps, and native API research.
  The [native bridge contract](prompt-navigation-native-contract.md) now records
  the implemented identity/key boundary and the precise missing capabilities in
  Claude 2.1.296 and Codex 0.162.1. Do not equate tested key admission with a
  qualified Windows hook, or a native message ID with a terminal position.
- **Pre-navigation baseline.** Before Phase 3, retain an unmodified build and
  record its commit and full informational version. Run relevant clipboard
  checks and actual Ctrl+Alt+J delivery, disabled/paused behavior, and conflict
  cases on it, then repeat them on the integrated build. Recheck
  `docs/v0.1-proof-ledger.md`: its 19 manual rows do not include the join
  hotkey, and `docs/v0.1-test-runbook.md` identifies stale RichFormat/History
  expectations that must be repaired before those cases count as evidence.
  Keep the full v0.1 proof work under its existing plan and report actual
  results and unmet prerequisites. It does not block the isolated Phase 1 probe.
- **Product placement.** `CONTRIBUTING.md` defines margin cleanup and line
  rejoining as the whole product, and the README promises no global keyboard
  hook. The reusable parts are the tray, JSON configuration, and hotkey
  plumbing. After Phase 1 establishes the input mechanism, decide before
  Phase 3 whether navigation ships inside Clici or as a separate executable,
  based on that evidence and the intended product scope. The Phase 3 layout
  assumes the in-process option. A separate executable needs explicit
  configuration ownership and single-instance/lifecycle rules; two processes
  must not independently overwrite the same cached configuration. A native
  settings-only alternative requires its own supported-behavior decision.

## Existing work and starting point

| Precedent | What to reuse or learn |
| --- | --- |
| [Claude fullscreen navigation](https://code.claude.com/docs/en/fullscreen#search-and-review-the-conversation) | Ctrl+O followed by `{` or `}` already navigates prompts in the fullscreen transcript. |
| [Claude prompt-jump mod](https://github.com/ruanss4/prompt-jump) | Tracks user-message IDs and calls the renderer's scroll API. Its previous/next controls and one waypoint per prompt are useful interaction references. Fullscreen is required. |
| [Codex Ctrl+Up/Down implementation](https://github.com/openai/codex/issues/47300) | A community fork implements these keys through backtrack browsing. That interface retains Enter-to-rewind and is not the behavior Clici should invoke. |
| [Windows Terminal shell integration](https://learn.microsoft.com/en-us/windows/terminal/tutorials/shell-integration) | Native `scrollToMark` actions provide the desired motion once marks exist. Shell integration alone does not identify each conversation turn. |
| [COM UI Automation ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-scrollintoview) | Supports aligning a text range to the viewport top. Windows Terminal implements this in [UiaTextRangeBase.cpp](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/types/UiaTextRangeBase.cpp). Live feasibility remains to be tested. |

The research baseline found Windows Terminal 1.24.12741.0 running, with npm
packages Claude Code 2.1.296 and Codex 0.162.1 installed. Claude's user settings
select `tui=default` and set `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`. Record
actual executable versions, launch options, and renderer modes for both agents
when running the prototype. Codex's renderer mode was not recorded in the
research baseline; do not infer it from the installed version alone.

Clici already has a tray lifecycle, JSON configuration, a clipboard listener,
and a global join-lines hotkey. Its foreground provider returns a process name,
which cannot distinguish terminal panes. `HotkeyParser.TryParseNavigation`
accepts modified arrows; the existing global join-hotkey grammar is unchanged.
Neither parser establishes the pane scope required for navigation.

## User behavior and scope

| Requirement | Expected behavior |
| --- | --- |
| NAV-001 Previous prompt | Alt+Shift+Comma brings the owning user prompt to the top if its beginning is above the viewport. If the last jump targeted that prompt, another press goes to the preceding user prompt. From live output, the first press reaches the latest user prompt. |
| NAV-002 Next prompt | Alt+Shift+Period advances to the next user prompt. Advancing past the proven newest prompt returns to live output, which follows subsequently appended output. At live output it does nothing. Phase 1 must demonstrate that mechanism. |
| NAV-003 Reading only | Navigation preserves the input draft, selection, clipboard, and conversation. It never submits text, recalls input history, rewinds, forks, or interrupts a running agent. |
| NAV-004 Correct pane | Only the active eligible terminal pane moves. Unsupported apps, ordinary shells, and other panes keep their normal keyboard behavior. |
| NAV-005 Stable targets | Repeated prompts remain separate waypoints. Resizing, reflow, buffer eviction, session changes, and redraws invalidate stale targets before scrolling. |
| NAV-006 Optional feature | Navigation is disabled by default and has its own enable switch and configurable shortcuts. Turning it off removes interception immediately and invalidates pending work. Existing clipboard controls retain their meaning. |
| NAV-007 Clear limits | When the pane, renderer, or prompt boundary cannot be established, leave the viewport unchanged and expose a concise reason in the tray status. |

NAV-001 cannot infer "already at this prompt" from the viewport alone. Near the
buffer end a prompt may never reach the top row. Keep a cursor for the last
verified target occurrence and its pane/session identity. A fresh snapshot is
an observation revision, not automatically a new session/layout epoch. Preserve
the cursor across append-only output only when its occurrence can be revalidated
by position and contextual evidence; identical text alone is not identity.
Reflow, eviction, session changes, or ambiguous redraw invalidate stale ranges.

| State before the key | Alt+Shift+Comma | Alt+Shift+Period |
| --- | --- | --- |
| Live, proven by viewport/follow state | Latest user prompt | No-op |
| Browsing a revalidated last jump target | Previous occurrence | Next occurrence, or live only when no later prompt is proven |
| No cursor after manual scrolling; viewport and boundaries known | Owning prompt if its start is above the viewport; otherwise the preceding prompt | First prompt beginning below the viewport top, or proven live boundary |
| Target or coverage cannot be established | Refuse without scrolling | Refuse without scrolling |

At the true oldest retained prompt, Up is a no-op. At an artificial capture
limit, acquire adjacent bounded coverage or report the limit; do not claim the
history ended. In a positively eligible pane, navigation consumes its chords,
including no-ops and refusals after interception. Unknown/ineligible contexts
pass input through before interception; never replay a consumed chord.
Commit the cursor only after verifying the jump, retain it through the jump's
own viewport notifications, and clear it on return-to-live or manual scrolling.
Phase 1 must establish how those viewport changes are distinguished.

The initial feature covers retained scrollback in tested Windows Terminal
configurations. Fullscreen/alternate-screen agents, VS Code/Cursor terminals,
other terminal emulators, SSH, and WSL require separate qualification.
The adapter runs non-elevated. Elevated (administrator) Windows Terminal
windows are out of scope for that UI Automation client; treat them and
inaccessible panes as ineligible and verify shortcut pass-through. UIA access
restrictions do not prove that a global hotkey or low-level hook leaves their
input alone. Messages evicted from the buffer or outside the declared capture
coverage are unavailable; report a capture limit distinctly from eviction.
Do not change an agent's renderer automatically.

## Phase 1 Prove terminal access and shortcut handling

Build a small development probe under `tools/proof/prompt-navigation/`. Use
synthetic conversations in disposable test panes. Keep the probe independent
of Clici's clipboard pipeline. Use the COM UI Automation client
(`IUIAutomation`) intended for the shipped adapter. Enumerate patterns exposed
by the tested provider; do not assume `TextPattern2` is available or required.
Record the pinned/generated interop source and prove the probe builds with
`dotnet build` before relying on its evidence.

0. Test native marks before building the UI Automation probe. Use a disposable
   Windows Terminal instance with a verified independent settings store; a
   scratch profile alone does not isolate key bindings. Enable
   `autoMarkPrompts`, bind `scrollToMark` `previous`/`next` to Ctrl+Up/Down,
   and test both agents. Record OSC 133 shell integration and test both the
   intended shell and a clean shell without command marks: an existing OSC
   command mark can prevent subsequent Enter presses from adding marks.
   Check one mark per rendered user message, exact start-row alignment where
   possible, multiline entry, dialogs/pickers, redraw/reflow, and streaming.
   Test repeated jumps near the buffer end, Up from live with the latest
   prompt already visible, and Down past the last mark followed by new output.
   Check ordinary shells with and without marks. Bindings apply across
   profiles, so staying inside Windows Terminal does not satisfy NAV-004.
   Record results against every NAV requirement, including optional enable
   and status behavior. If native settings alone are useful, document that
   alternative and obtain an explicit scope decision before waiving any NAV
   requirement or replacing later implementation phases. Otherwise continue
   with step 1. Do not change the user's existing settings for this experiment.
1. Resolve the foreground Windows Terminal window and its focused text control.
   Establish pane identity from the window and UI Automation element, rather
   than treating every pane belonging to `WindowsTerminal.exe` as one session.
   Prove positive eligibility for the currently running supported renderer,
   not merely old transcript text. Define startup/enable discovery, refresh,
   expiry, and invalidation for agent start/exit, clear/resume, and renderer
   changes in the same focused pane. Unknown eligibility passes input through.
2. Read visible ranges and bounded retained text through UI Automation. Inspect
   available text attributes and layout evidence for Claude and Codex user
   messages. Include the current composer and assistant code that imitates
   prompt delimiters in the fixtures.
3. Find a known synthetic message and call `ScrollIntoView(true)`. Verify the
   actual viewport, including alignment near the buffer end where a full
   top-alignment may be impossible. Repeat in split panes and multiple tabs.
   Demonstrate return-to-live, then append output and verify continued follow
   with draft and selection preserved. A candidate is a fresh document range
   collapsed to its end with `ScrollIntoView(true)`; qualify it rather than
   assuming bottom-aligning the last text row restores follow mode.
4. Exercise resize/reflow, new streaming output, manual scrolling, clear/resume,
   repeated message text, and focus changes. Determine which text ranges can
   be reused and which must be reacquired.
   Include a full history buffer evicting rows during generation, cold start
   in an existing session, shell-to-agent-to-shell without changing focus,
   alternate-screen transitions, and stale transcripts left in ordinary shells.
5. Prove how shortcuts can be scoped to an eligible pane. `RegisterHotKey` is
   system-wide: checking focus after receiving `WM_HOTKEY` does not restore a
   chord already consumed in another application. Test focus-scoped registration
   and its transitions before choosing it. Switching panes or tabs inside one
   Windows Terminal window does not change the foreground window, so
   use UI Automation focus-changed events as a candidate alongside foreground
   events, and prove their coverage and timing rather than assuming them.
   Marshal registration changes to the owning thread. Press the key immediately
   after window/tab/split changes, including Terminal search UI, ordinary
   shells, and elevated windows. Bind requests to the pane and eligibility
   revision observed at interception. A later refusal cannot restore input
   already consumed; failure here means the mechanism does not meet NAV-004.

If registration cannot preserve normal input outside eligible panes, evaluate
a narrowly filtered keyboard hook. Its callback may only consult cached
eligibility, queue one of the two navigation actions, or pass input through;
it must never call UI Automation or retain typing. Install it on a dedicated
thread with its own message loop. Windows can silently remove a hook after
`LowLevelHooksTimeout` and provides no direct removal notification. Demonstrate
a bounded liveness/reinstallation policy that distinguishes idle input from
failure. Force a callback timeout in the disposable probe without changing the
system timeout, then verify recovery and normal input. Do not select the hook
without that evidence. This revises Clici's no-global-keyboard-hook claim and
must be documented explicitly.
The prototype must resolve this choice before integration. Do not silently
substitute different keys or ship unconditional navigation-shortcut registration.

Save environment details, synthetic fixtures, timings, and viewport captures
under the Git-ignored `artifacts/prompt-navigation/<run>/`. Include Terminal
history size, capture bounds/coverage, and exact renderer launch options.
Use only disposable synthetic sessions and crop captures to the target pane
in both Phase 1 and Phase 4. Prefer remotedesk/lugos.vision for captures when
available. Summarize results in
`docs/prompt-navigation-proof-report.md`.

**Exit condition:** Each declared agent configuration has evidence for current
session eligibility, correct pane targeting, recognizable user messages,
reliable scrolling/live-follow, and shortcut isolation. If an agent or renderer
lacks evidence, identify the missing capability and explicitly narrow the
release scope; do not claim support for both by default. If neither renderer
is viable and native marks also failed, record an alternative using app-native
anchors or deliberately emitted terminal marks before building the integrated
feature.
A hook firing at submission time alone does not prove that a mark lands on the
rendered user-message line.

## Phase 2 Implement message recognition and navigation decisions

Add pure navigation decisions under `src/clici.Core/Navigation/`, with fixtures
and tests in `tests/clici.Core.Tests/`.

The decision core now requires native agent/session/message IDs independently
of position/context evidence. `NavigationInputQueue` implements bounded admission,
one action per physical press, deadlines and invalidation. The Claude probe
collects IDs but intentionally reports no navigation eligibility. Actual renderer
recognition, pane ownership, COM coordination and OS key delivery remain below.

- Define a bounded terminal snapshot with capture origin/extent, actual versus
  artificial edge flags, complete-message boundaries, ordered prompt
  candidates, viewport position, and explicit success/refusal/limit results.
  A local snapshot index is not a stable buffer position. At an artificial
  edge, acquire adjacent bounded coverage or refuse; return to live only after
  proving that no later prompt exists, never just because the slice ran out.
- Implement separate Claude and Codex recognizers using the evidence established
  in Phase 1. A leading `>` or similar glyph alone is insufficient: shell
  prompts, quoted replies, and code examples can contain it too.
- Exclude the editable composer, tool output, permission dialogs, and unsupported
  renderer layouts. Only rendered user messages become waypoints.
- Select previous/next targets by position in the active pane's snapshot. Do not
  identify a repeated prompt by searching for its text and taking the first match.
- Model Live, Browsing, and Unknown states using the transition table above.
  Keep observation revisions separate from pane/session/layout epochs.
  Revalidate target occurrences across refresh/append; invalidate coordinates
  on reflow/eviction and refuse if identity cannot be re-established.
- Bind work to its request, pane, eligibility revision, and deadline. Disable,
  manual scroll, session changes, and focus changes invalidate queued work.
  Before scrolling, recheck eligibility and target position/context; read back
  the resulting viewport before committing a cursor. Discard late results;
  this cannot undo a COM scroll that has already started.

Start with on-demand bounded snapshots. Optimize caching only after measurements
justify it. Avoid scanning private session-history directories as a substitute
for establishing the active pane. If transcript metadata becomes necessary,
first define an explicit pane-to-session binding and its lifecycle.

**Exit condition:** Tests cover previous/next semantics, duplicate prompts,
multiline and Unicode content, false prompt examples, composer exclusion,
and stale-target refusal. Include two distinct presses across refresh/append
near the buffer end, own-scroll versus manual-scroll notifications, pending
reads followed by disable/focus change, and held repeats versus distinct presses.
Test duplicate/partial prompts across capture cutoffs, a newer prompt beyond
the slice, long responses without a captured prompt, and true history ends.

## Phase 3 Integrate an optional Windows navigation service

| Area | Proposed responsibility |
| --- | --- |
| `src/clici.App/Navigation/` | Interfaces and Windows Terminal adapter for pane discovery, snapshots, range resolution, and scrolling. |
| `src/clici.App/Input/` | Navigation-key parsing and the shortcut mechanism qualified in Phase 1. Keep it independent of clipboard notifications. |
| `src/clici.App/TrayApplicationContext.cs` | Own service lifecycle, immediate runtime enable/disable, and concise status. Disable removes interception and invalidates pending work. Manual JSON shortcut edits take effect after restart. Persist the toggle only when persistence is allowed, matching existing tray behavior. |
| `src/clici.Core/Configuration/CliciConfiguration.cs` | Add `promptNavigationEnabled` defaulting to `false`, `previousPromptHotkey` defaulting to `Alt+Shift+Comma`, and `nextPromptHotkey` defaulting to `Alt+Shift+Period`. |
| Configuration store and tests | Increment the schema version when fields ship; preserve old-file defaults and malformed-file behavior. Reject conflicting navigation chords without disabling clipboard cleanup. |

Run UI Automation on an MTA worker that owns no windows; add/remove event
handlers on that same thread. Keep it off the tray and any hook message loop.
Use one bounded queue, coalescing held-key repeats while preserving distinct
presses or explicitly refusing excess work. Apply a request deadline and the
eligibility/target checks from Phase 2 immediately before moving the viewport.
A timeout does not cancel an in-flight COM call: do not spawn replacement
workers indefinitely when a provider hangs. Suspend the adapter, report the
failure, and keep the tray and clipboard service responsive.

Keep terminal text transient in memory. Diagnostics may record result codes,
counts, timings, and supported-version information, but no prompt or terminal
contents. Navigation must not use the clipboard as a transport or send
synthetic conversation commands.

**Exit condition:** Clici applies the tray toggle immediately, handles
configuration/input conflicts, survives adapter failures, and shuts down
without leaving shortcuts registered. Pending results do not re-enable a
disabled service. Existing clipboard tests remain green.

## Phase 4 Validate the complete user interaction

Run the following matrix for every qualified agent configuration:

| Case | Evidence required |
| --- | --- |
| Long conversation | Repeated previous/next shortcuts land on the expected prompts in order and return to live output. |
| Ambiguous text | Assistant code, shell syntax, echoed prompts, repeated prompts, and dialog text are not mistaken for a user-message boundary. |
| Layout changes | Wrapped text, Unicode, window resizing, and font/zoom changes preserve correct targeting or produce a safe refusal. |
| Active generation | Draft and selection survive streaming, including a full buffer evicting rows. Repeated presses preserve cursor continuity; returning live follows subsequent output. |
| Multiple panes | Immediate keys after window/tab/split changes move only the intended eligible pane and preserve input in other contexts. |
| Other input contexts | Editors, browsers, ordinary shells, Terminal search UI, unsupported/fullscreen agents, elevated/inaccessible windows, and disabled navigation retain normal shortcuts. Include agent exit leaving old transcript text and renderer changes without a focus change. |
| History and capture changes | Eviction, clear, resume, redraw, partial prompts, and artificial capture edges never reuse stale anchors or skip unseen prompts. |
| Failure and coexistence | Conflicts, timeouts, restart, disable during pending work, and exit leave Clici responsive; compare clipboard and actual Ctrl+Alt+J behavior with the pinned baseline, including existing Enabled/Pause semantics. If using a hook, include forced-timeout recovery. |

Use fakes for coordinator ordering and failures, pure fixtures for recognition,
and actual Windows Terminal sessions for UI Automation and keyboard behavior.
Screenshots must demonstrate the requested prompt and the viewport after the
jump; a successful API return alone is insufficient.

Measure cold and repeated navigation against the declared Terminal history
size and capture limits. Treat 250 ms for repeated jumps as a measured target;
fail a stalled request within one second without blocking clipboard processing.
Record actual limits, coverage/refusal behavior, and timings in the proof report
rather than claiming unlimited scrollback or cancellation of in-flight COM work.

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

The feature is complete when every declared agent configuration passes the live
matrix, unsupported contexts retain normal input, clipboard behavior still
passes its existing checks, and the published support claims match the evidence.

**First implementation batch:** the isolated Phase 1 step 0 native-marks test
and proof report, then the COM probe if native settings cannot meet the agreed
behavior. Settle current-session eligibility, terminal access, recognition,
scrolling/live-follow, shortcut isolation, and product placement before adding
a permanent navigation service to Clici.

**Batch outcome (2026-10-16):** the native route fails NAV-001/NAV-004. The COM
probe reads both tested renderers, scrolls synthetic/Claude text, restores
synthetic live follow, and targets one split pane. Agent exit can leave the same
pane identity without a focus event, and observed text attributes do not identify
roles. The independent core now has 24 navigation tests; recognizers and service
integration remain unimplemented. Continue with the agent-native qualification
in the proof report, preserving existing launch commands. Do not treat this
partial Phase 1/2 batch as permission to register navigation shortcuts globally.
