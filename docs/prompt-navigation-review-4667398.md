# Prompt navigation review of 4667398

**Verdict:** proceed with the isolated Phase 1 experiment after the plan
corrections below. Integration remains conditional on demonstrating reliable
pane eligibility, message recognition, scrolling, and input isolation. This
review does not establish that the feature works in a live terminal.

## Subject and provenance

- Subject: `docs/prompt-navigation-plan.md` at
  `46673985465d1c1375a2691a1ede093a25416883`.
- Subject SHA-256:
  `2e623b9104318aaeafc45378aa2784f3da0470faa83694627c23b9a82797b13b`.
- Autowork run: `20261016-023833-cross-review`, completed successfully using
  `claude-opus`, model `opus`, high effort, adversarial mode. One invocation;
  the reviewer received a fixed packet and had no tools. It returned 18
  findings, which are adjudicated below rather than accepted automatically.
- Roundtable: `clici-prompt-nav-4667398`, technique `dimensional-review`.
  Autowork's unedited response was imported with provenance; independent
  platform and behavior reviewers checked Microsoft documentation/source and
  Clici's implementation/runbooks. The chair reconciled their findings.
- Local evidence root:
  `artifacts/prompt-navigation-review-4667398-20261015/`. It contains the
  source archive, manifest, original Autowork response, WEIR captures, reviewer
  drafts, and the CLI-managed Roundtable session. These files are ignored by
  Git. This report preserves the decisions and public source links.
- The subject's [CI run](https://github.com/mdn87/clici/actions/runs/38840782822)
  succeeded. That does not erase the earlier bypass of required checks; plan
  corrections should land through a PR after `build-test` and `format` pass.

## Consolidated corrections

### 1. Give the native-marks experiment a complete acceptance test

Keep native marks as the cheapest first experiment, using the current
`autoMarkPrompts` setting. Record shell OSC integration state: in Terminal
1.24.12741.0, Enter handling calls `StartOutput`, and the text buffer can
decline a new mark while an existing marked command is still running. An
agent conversation is not automatically a sequence of shell commands.
Sources: [Terminal input handling](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalCore/Terminal.cpp#L743),
[text buffer marks](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/buffer/out/textBuffer.cpp).

Test both agents with the observed OSC setup, rendered-message alignment,
false marks from dialogs/multiline entry, repeated navigation near the bottom,
return-to-live following new output, redraw/reflow, and ordinary-shell input.
A scratch profile does not isolate global key bindings. Use a verified
independent settings store for the experiment; any changes to the user's
existing settings remain a separate authorized action. A native result must
still be compared with NAV-001 through NAV-007. A settings-only alternative
needs an explicit decision about changed behavior; it cannot silently waive
NAV-004, NAV-006, or NAV-007.

### 2. Establish eligibility before consuming a shortcut

Historical prompt text and a `WindowsTerminal.exe` process name do not prove
that an agent still owns the focused pane. Add discovery on enable/startup,
positive evidence of the current renderer/session, cache expiry, and
invalidation for agent start/exit and renderer changes without a focus change.
Unknown, inaccessible, and unsupported panes must retain input.

Bind each request to the pane and eligibility revision at interception.
Measure immediate-key transitions between windows, tabs, and splits; a worker
refusal cannot restore a chord already consumed. Focus events alone are not a
complete eligibility lifecycle. Keep shortcut isolation as a Phase 1 proof
requirement rather than assuming either registration or a hook solves it.

### 3. Specify navigation state separately from fresh snapshots

Define Live, Browsing, and Unknown states. A new observation must not itself
erase the last successful target. Revalidate a target occurrence across
append-only output using pane/session identity, position and contextual
evidence; identical text is not identity. Keep observation revisions separate
from epochs that invalidate buffer coordinates.

Distinguish the viewport change produced by a successful jump from manual
scrolling. Invalidate pending work after disable, session/focus changes, manual
scrolling, or expiry. Recheck target evidence before scrolling and read back
the viewport before committing the cursor. A timed-out COM operation may still
finish; discarding its result does not cancel a scroll already in progress.

### 4. Distinguish capture limits from conversation boundaries

Snapshots need origin, coverage, complete-message boundaries, and flags for
actual versus artificial edges. At an artificial edge, acquire adjacent
bounded coverage or report a limit. Never treat a missing candidate in a
partial snapshot as proof that the next action should jump to live output.
Test duplicate and multiline prompts straddling a cutoff and long responses
with no captured prompt.

Return-to-live must visibly follow output appended after the jump, preserve
the draft and selection, and clear the navigation cursor. Name and test its
mechanism during Phase 1; a successful `ScrollIntoView` call alone is not
evidence of that behavior.

### 5. Correct Windows assumptions without inventing guarantees

Use the COM client consistently in the probe and product, recording the
interop source and proving command-line builds. Link the COM scrolling API.
Feature availability must be checked on the actual provider; choosing COM
does not prove the needed patterns are implemented. The inspected Terminal
peer advertises Text, and the plan's read/scroll operations do not establish
a need for TextPattern2. The claim that all managed-client evidence would be
non-transferable is too broad.
Sources: [Terminal automation peer](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalControl/TermControlAutomationPeer.cpp#L292),
[COM ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-scrollintoview).

Windows documents no direct way to discover silent low-level hook removal.
If the hook remains necessary, use a dedicated message-loop thread and prove
a bounded liveness/reinstallation policy, including a deliberate timeout in
a disposable test build. Do not promise a nonexistent removal notification
or change the system timeout to run the test.
Source: [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc).

Run navigation non-elevated and exclude administrator/inaccessible panes by
policy. Test shortcut pass-through for both candidate input mechanisms; UIA
security restrictions do not by themselves prove that a global hotkey cannot
consume input in an elevated window.
Source: [UI Automation security](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview).

### 6. Pin a useful baseline and resolve configuration ownership

Retain an identified build without navigation before integration, then compare
the relevant clipboard behavior and actual Ctrl+Alt+J delivery on both builds.
The old ledger has no join-hotkey case, and the existing runbook explicitly
requires repairing stale RichFormat/History expectations before using those
cases. The complete v0.1 ledger retains its existing scope; it need not block
the isolated terminal experiment.

Choose product placement after Phase 1 from demonstrated capabilities and
Clici's stated scope. Remove the unsupported Defender/EDR prediction. If a
separate executable is selected, define configuration ownership and lifecycle
before sharing code that writes an entire cached configuration.

Make the navigation tray toggle take effect immediately, with pending work
invalidated on disable. Manual JSON shortcut edits may follow Clici's existing
restart policy. Persist changes only when configuration persistence is allowed.

## Autowork finding dispositions

| Finding | Disposition |
| --- | --- |
| PNAV-01 | Accept the missing native-path acceptance criteria and deliverable decision. Native behavior remains an experiment. |
| PNAV-02 | Accept settings isolation; a scratch profile is insufficient. No settings changes were made in this review. |
| PNAV-03 | Accept eligibility lifecycle and interception/refusal distinction; also supported by behavior B1. |
| PNAV-04 | Accept runtime renderer eligibility tests. Do not infer that every classic Claude Ctrl+O view enters an alternate screen. |
| PNAV-05 | Accept the undefined snapshot/cursor lifetime; use occurrence revalidation, not the suggested fingerprint alone. |
| PNAV-06 | Accept an eviction-under-streaming test and pre/post-scroll validation. The specific wrong-row race is a hypothesis, not an observed bug. |
| PNAV-07 | Accept live-follow acceptance and defined oldest/live key behavior. Do not send typing to establish live state. |
| PNAV-08 | Accept explicit non-elevated scope and shortcut tests. Replace the blanket hook/UIPI assertion with the narrower documented boundary. |
| PNAV-09 | Accept dedicated-thread and recovery proof requirements. Direct hook-removal detection is not available; the proposed heuristic is unproven. |
| PNAV-10 | Accept registration-owner marshalling and immediate-key transition tests. A measured delay is not permission to violate NAV-004. |
| PNAV-11 | Accept a pinned pre-navigation baseline with current clipboard and join-hotkey checks; retain the legacy ledger's separate obligations. |
| PNAV-12 | Accept removal of the unsupported EDR prediction. Placement follows Phase 1 evidence; no new approval gate is needed to conduct the probe. |
| PNAV-13 | Accept recording and building the COM interop dependency. There is no observed interop build failure. |
| PNAV-14 | Accept explicit per-agent renderer/configuration recording and a declared qualified scope. No evidence here proves installed Codex is unusable. |
| PNAV-15 | Resolve the toggle immediately at runtime; distinguish manual JSON edits. |
| PNAV-16 | Accept explicit capture coverage/limits. Keep 250 ms as a measured target rather than inventing a hard release threshold without benchmark evidence. |
| PNAV-17 | The subject already requires synthetic disposable conversations. Clarify that the same restriction applies to Phase 4 captures; do not invent a new privacy blocker. |
| PNAV-18 | Accept the COM documentation link for consistency. |

The behavior review's B1-B4 map to corrections 2, 3, 4, and 6. Other accepted
refinements specify runtime configuration and live-follow verification.
The platform review's two P2 findings map to corrections 5 and 1. Its other
conclusions narrow the COM and elevation claims and keep focus-event coverage
and return-to-live as experiments. Use an MTA UIA worker without window
ownership, subscribing and unsubscribing on the same thread, and marshal
registration changes to the hotkey-owning thread.
Sources: [UIA threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading),
[UIA event coverage](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsforclients).

Two platform concerns are explicitly not new defects: the old
`experimental.autoMarkPrompts` spelling is still migrated by the inspected
[Terminal version](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalSettingsModel/Profile.cpp#L198),
and native next-mark navigation already has a bottom-of-buffer fallback in
[ControlCore](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalControl/ControlCore.cpp#L2642).
The latter is useful source evidence, but live-follow still needs the stated
acceptance test. A candidate for the COM probe is a fresh document range
collapsed to its end with `ScrollIntoView(true)`; this is not yet qualified.

## Next implementation batch

Run only the isolated native-marks experiment first and write the proof report
with exact Terminal/agent versions, renderer modes, shell OSC state, settings
isolation, and NAV acceptance results. If it cannot meet the agreed behavior,
build the small COM probe to test eligibility, bounded text access, scrolling,
live-follow, and input isolation. Do not integrate a permanent service until
those experiments establish the supported scope and mechanism.

The [revised plan](prompt-navigation-plan.md) incorporates these documentation
corrections. No navigation implementation, live terminal proof, manual ledger
run, software installation, or user-settings change is claimed by this review.
