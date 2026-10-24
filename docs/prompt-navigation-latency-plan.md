# Restore responsive Codex typing with prompt navigation

Status: repair implemented, verified, merged in PR #47, and installed with Matt's approval. Physical typing and both shortcuts confirmed working.
Date: 2026-10-20. Source baseline: `cd9ca5f`.

## Outcome and boundaries

Keep Alt+Shift+Comma / Alt+Shift+Period for read-only prompt navigation in
Windows Terminal classic scrollback, without making typing wait for Clici.
Handled navigation must leave drafts, selections, clipboard contents, and conversation history
unchanged. Never rewind a conversation, replay a swallowed key, or intercept
keys in an unqualified pane. Preserve NAV-001 through NAV-007 in
`docs/prompt-navigation-plan.md`.

Work is limited to the renderer bridge, its hook, native Codex integration,
tests, and their documentation. Claude shares the helper, so shared changes
must retain its existing protocol or explicitly negotiate a new version.
Do not switch terminals, replace the clipboard tray, upgrade Codex, or
automatically restore global shell integration. Installed integration is
currently disabled; existing shells can still have inherited bridge variables.

This document is the implementation plan and review subject. The review is
one round, read-only, on the `claude-fable` route. Record findings and their
disposition; address concrete findings without commissioning a second review.
See [review disposition](prompt-navigation-latency-review.md) for all 16 findings,
the exact reviewed manifest, and the distinction between review delivery and
implementation verification. This planning milestone does not establish a fix.

## Original diagnosis (before implementation)

- An isolated fork of the affected conversation, using the same patched Codex
  binary with Clici environment variables removed, typed quickly according to
  Matt. This implicates the bridge but does not measure individual COM calls.
- `codex-renderer/protocol.rs`, `Client::start` and `Client::navigate`, wait up
  to 1,000 ms for the helper. `Client::stop` kills and waits for it. Pipe I/O is
  on a worker already, but callers still wait for the result.
- `codex-renderer/clici_bridge.rs`, `publish`, `event`, and `ensure_client`,
  invoke those operations while holding the native bridge state mutex.
- The native patch schedules another frame after 100 ms while keyboard
  navigation is active. `publish` does not deduplicate keyboard-enabled frames.
- `RendererKeyboardHook.cs` invalidates its single revision on ordinary
  keydown. `RendererBridge.cs` includes that revision in cached qualification,
  so ordinary typing can cause another full scan.
- `RendererBridge.Observe` reads up to 1 MiB / 4,096 rows and uses repeated
  `RowAt(document, index)` calls from the document origin. The traversal may
  be disproportionately costly; its scaling has not been measured.
- Helpers initially discover the foreground pane and establish ownership by
  matching painted anchors. Multiple sessions can scan the same foreground
  terminal before a mismatch establishes that it is not theirs.
- The tray Enabled setting controls clipboard cleanup, not these helpers.

All paths above are below `tools/proof/prompt-navigation/` unless qualified.
Existing logs contain expired input leases and incomplete-anchor refusals;
neither is itself proof of the exact per-keystroke timing mechanism.

## Alternatives considered

Native Windows Terminal OSC 133 marks / `scrollToMark` remain attractive because
they avoid UIA navigation. The existing `docs/prompt-navigation-proof-report.md`
records tests that skipped the newest prompt and reached a launcher command;
global Terminal bindings also lack the required native composer eligibility.
Those results do not prove that every renderer-mark design is impossible, but
there is no verified replacement satisfying NAV-001 through NAV-007 today.
Changing the terminal alone does not supply correct renderer prompt marks.

The researched `ralyodio/codex-dx` arrow-navigation branch uses backtrack preview
and rewind semantics and requires an idle, empty composer. `ruanss4/prompt-jump`
uses Claude's native full-screen scroll API; it is not a Codex classic-scrollback
implementation. Neither is a drop-in replacement for the requested behavior.

## Architecture decisions

### 1. Remove helper work from the native input/render path

Keep one background supervisor per Codex process. It exclusively owns helper
startup, reply validation, deadlines, termination, and restart backoff. Retain
a separate pipe I/O worker: a thread blocked in read/write cannot also enforce
its own timeout. On deadline expiry the supervisor terminates its child, closing
the pipe endpoints, and observes worker completion without joining on the UI
thread. Do not spawn unlimited replacement workers if teardown fails; latch
disabled and report that failure. The input/render thread only publishes a bounded
native snapshot and drains bounded notifications; it never waits for helper readiness,
pipe completion, process exit, a thread join, or a UIA operation.

Use a latest-state mailbox: at most one active observation and one replacement
snapshot. Repeated draws replace the pending snapshot rather than accumulate
work. Store immutable prompt bindings behind shared ownership and replace them
only when the native prompt list changes. Bound results and diagnostics too.
The supervisor never takes the native State mutex. Any shared lock protects
only a short swap; the UI uses a nonblocking acquisition and retries publication
on its next scheduled state check if the mailbox is busy.

Tag traffic with process incarnation, session, epoch, and sequence. A restarted
helper cannot reuse an old generation, lease, reply, or queued press. Deadlines
are derived from the actual native observation time; dequeuing or sending an
old snapshot must not refresh it. Validate before sending as well as receiving.
Restart exhaustion is a latched disabled state, not reset by the next draw.
Retain the budget of 10 replacements and delays of 1, 2, 4, 8, 16, then at most
30 seconds. A new Codex process is the explicit reset for this repair. Report
exhaustion once in the Codex log with a clear restart instruction and retain it
in diagnostic status; no new settings UI is required.

Assign the child to a Windows Job with kill-on-close before it can install a
hook (create suspended, assign, then resume). Fail to start interception if
assignment fails. Own the Job handle for the native process lifetime, never
inherit it into the child. Parent exit must terminate even a hung helper without
a UI-thread wait; normal helper exit uninstalls hooks. Verify no orphan survives.

The optional `CLICI_NATIVE_KEY_PROBE` path currently requires a synchronous
handled result. Remove its interception in the revised integration and document
that diagnostic path as unsupported; production Alt+Shift bindings remain on
the pre-Terminal hook. Do not silently consume native Ctrl+Up/Down asynchronously.

### 2. Preserve revocation while making delivery asynchronous

A queued positive observation must not restore eligibility after a modal,
focus change, resize, session switch, shutdown, or disable. A fresh snapshot
after an admitted shortcut remains mandatory before scrolling.

Use an unnamed shared section between the native process and its own helper,
separately from the potentially stalled observation pipe. An immutable header
holds ABI version and process incarnation. The sole mutable field is one aligned
64-bit atomic word: `(admission_epoch << 1) | eligible`. The native process is
the sole writer; duplicate only a read-only mapping handle into the child and
verify that the child cannot map it writable. Bind the section, private pipes,
and child to the pinned parent handle. Do not claim protection from arbitrary
malicious code running with the same user's debugging privileges.

The admission epoch changes on invalidating input/context events, including
modal, focus, resize, session, shutdown, and disable transitions. It does NOT
change for a heartbeat that reconfirms the same state. The separate publication
sequence increases for every snapshot. On epoch exhaustion, disable instead of
wrapping. A session switch changes the epoch before a new positive snapshot.

Native invalidation writes the admission word directly, independently of the
mailbox lock, before returning to normal input. Specify atomic interoperation
for the supported Windows x64 ABI and verify Rust-write/C#-read behavior. The
hook performs a single bounded atomic read; no spin, COM, I/O, allocation, or
logging. Hook-local input revisions still cancel presses immediately.

Every hook admission, lease publication, and pre-scroll check requires a matching
admission epoch in addition to existing parent, focus, pane, and expiry checks.
A stale observation cannot reauthorize interception even if its reply arrives
later. Expiry remains at most 750 ms; no worker extends it from cached state.
Observation/expiry timestamps stay in pipe messages and use the existing common
Windows millisecond clock, `GetTickCount64` / `Environment.TickCount64`, with a
cross-language agreement test. Do not compare Rust Instant and .NET Stopwatch.
Confirmation must be observed strictly after the admitted key; a later sequence
alone is insufficient. Parent exit or incompatible/missing records mean no
interception and no navigation: keys pass through.

Immediate revocation is retained despite the review's suggested async-only
first release. Counterexample: key admitted at t0; positive confirmation captured
at t1; modal opens at t2; helper processes the queued t1 confirmation at t3 before
expiry. It is post-key and within the lease yet no longer authorizes scrolling.
In-band expiry alone cannot reject it. Benchmarks may isolate async work, but a
keyboard-enabled candidate requires the simpler atomic revocation contract.

Version this capability explicitly. New Codex requires it and refuses keyboard
interception with an old helper. Keep the legacy Claude protocol isolated until
Claude can publish the same capability; do not let a legacy observation renew
a new Codex lease. Legacy Claude retains the existing single-revision cache and
invalidation behavior; gate the split-revision/certificate path to the new
protocol. Test ordinary typing and pane changes in the legacy path too.

### 3. Separate input eligibility from terminal layout and ownership

Maintain distinct admission and layout revisions. Ordinary keys immediately
invalidate shortcut eligibility and cancel unexecuted presses, as today, but
do not on their own discard a previously verified pane/anchor certificate.
Focus, mouse, resize, process, session, and prompt-map changes invalidate the
appropriate ownership/layout state. Unsupported contexts remain pass-through.

A certificate contains the exact HWND, TermControl runtime ID, process/session
incarnation, prompt-binding generation, geometry, and a private painted-anchor
witness. Before reusing it, check current foreground, exact focused pane,
geometry, parent liveness, native admission epoch, and a bounded witness
range. Never treat HWND, a title, or CurrentHasKeyboardFocus alone as ownership.
All UIA and witness work runs on the helper's COM worker, never in a low-level
keyboard/mouse callback or native input thread. The hook uses only its cached
positive lease, atomic admission word, cheap Win32 foreground/parent checks,
and bounded local queue operations. While qualification is being recomputed,
withdraw the positive lease. Provider events may invalidate/wake the worker,
but must not synchronously call back into Codex or traverse Terminal themselves.
If the provider cannot cheaply verify the witness, leave navigation unavailable
until a bounded discovery attempt succeeds; do not scan on every key instead.

Retain a fresh full map/viewport check before an actual scroll, including the
existing final document/selection/readback checks. Cached ownership is not
permission to scroll to a stale row. Reflow, eviction, duplicate text, and
changing output must continue to refuse or rebuild ambiguous targets.

For unbound or mismatched helpers, permit one bounded discovery attempt per
foreground/pane change or native prompt-map change, with a one-second minimum
interval under repeated invalidations. Ordinary typing does not trigger discovery.
Retain a slow retry (at most once per second) for missed provider events while
unqualified. A helper with a known nonfocused pane performs no document scan.
Count and report discovery attempts so multi-session contention is measurable.

### 4. Replace forced redraw polling with explicit state observation

Use native render/session/modal/focus/resize events as the primary publication
triggers. Helper notifications request fresh native state through a bounded
wake/event path; they never request an unconditional full terminal redraw.
Integrate a timer event that reevaluates native input ownership without drawing
solely for bridge maintenance. Start with the existing 100 ms cadence for
eligibility freshness, then slow inactive/unqualified maintenance to one second.
The timer samples actual current native state; replaying a cached observation
with a new timestamp is forbidden. When no valid lease exists, shortcuts pass.

Focus authority is conjunctive: native composer/modal state and terminal focus
reporting must agree with helper foreground and exact-pane checks. The pinned
Codex TUI already executes `EnableFocusChange` and maps FocusGained/FocusLost
in `tui/event_stream.rs`. Preserve that path and make native FocusLost revoke
eligibility until FocusGained plus fresh qualification. Missing or unreliable
focus reporting cannot be treated as positive evidence.

The one-second discovery retry applies only after the positive lease has been
withdrawn. It is not a one-second permission to reuse a stale certificate.
Active focus verification retains the 100 ms maintenance cadence and existing
750 ms maximum lease; the shared word does not make OS event delivery atomic.
Test a lost UIA focus event during a native-reported tab switch, and the converse,
plus suppressed-event and programmatic-switch cases. A cached runtime ID alone
does not prove current focus. Do not loosen NAV-004 to accept wrong-pane key
consumption: any context that cannot establish the required focus evidence stays
unsupported with interception disabled. Record actual event-delivery limitations
instead of claiming that finite leases prove instantaneous focus awareness.

This deliberately keeps a lightweight watchdog. Hooks alone cannot prove native
composer eligibility or guarantee delivery of every terminal/provider event.
Full UIA scans are event-driven qualification or navigation work, not heartbeat
work. Measure remaining idle cost before considering any additional interval
change. Avoid changing traversal algorithms until scan timing shows they matter.

## Implementation order

1. **Reproduce and instrument.** Extend the fake pipe helper and isolated
   portable-Terminal harness. Record monotonic event arrival, native publication
   duration, frame flush, helper startup/round-trip, scan count/duration, lease
   refusal reason, queue depth, and restart count. Use synthetic prompts and
   metadata-only logs. Demonstrate the current blocking wait with delayed and
   never-ready helpers before changing production code.
2. **Implement asynchronous supervision plus revocation together.** Add focused
   failing tests, atomic-word ABI tests in Rust and C#, and stale-epoch
   tests first. Then replace the synchronous integration, preserve backoff,
   bound mailboxes, and latch exhausted retries. Do not enable a partially
   asynchronous implementation that lacks out-of-band revocation.
3. **Reduce repeated work.** Separate revisions, add bounded witness validation
   and discovery scheduling, and replace forced redraws with state-only events.
   Test each against ordinary typing, passive output, stale panes, and several
   concurrent sessions. Keep uncertain navigation disabled rather than weaken
   identity checks. Preserve legacy Claude helper behavior where it cannot
   participate in the new native admission contract.
4. **Verify and package.** Build the pinned Codex source plus helper into a
   new isolated development bundle, update protocol/runbook documentation,
   and retain version/hash evidence. Run tests and the complete scoped diff
   review once. Land validated repository changes using normal repository CI;
   do not change runner policy or global installations.

## Acceptance evidence

Automated regression coverage must exercise:

- Helper startup hang, 1-second reply delay, pipe write stall, malformed or
  oversized reply, disconnect, stale sequence, and child exit. Native event
  handling/publication continues; queues remain bounded and no UI-thread wait
  or child cleanup occurs. Test real fake-helper processes, not just mock calls.
- Superseded snapshots, modal withdrawal behind an in-flight observation,
  epoch/session changes, atomic read/write and read-only handle access,
  epoch changes between admission and execution, helper restart, and latched
  retry exhaustion. Heartbeats that advance only publication sequence must not
  cancel valid navigation; include a successful scroll taking over one timer tick.
- An outstanding pipe write is broken by deadline-triggered termination; no
  repeated worker leak. Codex exits promptly with a hung helper and no helper
  survives the native process. Failed Job assignment installs no hooks.
- Fresh confirmation after an admitted key; repeat/release behavior and keys
  held before attachment; uncertainty passes through and no input is replayed.
- Ordinary typing invalidates admission but does not cause full scans after
  stable qualification. Changed layout/ownership cannot reuse that qualification.
- Draft, selection, clipboard, prompt identity, return-to-live, partial prompt,
  duplicate prompt, replay, compaction, reflow, and eviction behavior.
- Multiple tabs/windows, same HWND with a different focused pane, unsupported
  split panes, parent exit while Terminal survives, and a focused non-agent app.
  Suppress each focus signal independently and exercise two agent hooks: an old
  session must not steal the focused session's shortcut.
- New Codex/old helper refusal and new helper/legacy Claude compatibility,
  including legacy typing, focus changes, and unchanged invalidation behavior.
- With a nonempty draft and no lease, compare both passed-through chords against
  bridge-off native behavior, including text, cursor, selection, and modal state.
  NAV-003 preservation applies to handled navigation; unhandled keys keep native
  semantics. Do not add a blanket native ESC/punctuation ignore rule: that would
  consume ordinary input and conflict with pass-through. Record whether the
  terminal/native binding edits the draft and expose that limitation truthfully.

Run the repository .NET Release build/tests, standalone locked Rust protocol
tests, installer tests when packaging changes affect them, and the patched
native Codex TUI build/tests. A protocol-only build does not validate the patch.

For live evidence, use only a disposable portable Terminal with synthetic
history at short, 1,000-row, and near-4,096-row sizes; include at least three
simultaneous agent sessions (two Codex and one legacy Claude). Compare bridge-off,
old bridge, and revised bridge on the same machine and terminal configuration.
Retain raw timings and version
metadata. Target p95 native event-to-flush below 50 ms and no bridge-attributable
stall over 100 ms, including when the helper is hung; compare against bridge-off
to distinguish Terminal/render cost. The maximum measured publication duration
must remain under 10 ms under the same load. These are acceptance targets, not
claims established by the current evidence; count samples and report p50/p95/max.
Record hook callback latency independently with a target p95 below 1 ms and max
below 10 ms under the same load, including a stalled COM worker. Collect timing
in bounded memory outside hot-path logging and distinguish scheduling delay from
callback execution time.

After qualification, a 100-character ordinary-typing run with no transcript or
layout changes must cause zero full-document scans. A known background session
must cause zero scans of the active session. Record idle CPU and scan counts
before and after; avoid a universal CPU threshold across different hardware.
Measure shortcut-to-scroll completion separately. With a healthy helper and
stable eligible pane, require at least 9 of 10 attempts in EACH direction at
EACH history size to reach the correct target within the existing deadline.
An always-refusing bridge fails acceptance. During fault/stale-context tests,
require refusal without a wrong scroll or typing stall. If large histories
miss the healthy success floor, instrument and improve the scan traversal before
claiming the outcome complete; preserve bounds and identity checks.

Injected-input fixtures do not prove physical key-hook or IME behavior. Finish
automated checks first, then request only the necessary operator observations:
physical typing, both shortcut directions with a draft, IME, and tab switching
in the isolated candidate bundle, including no-lease native pass-through.
Do not close existing chats or disturb drafts.

## Rollout and rollback

Keep the installed integration disabled during development. Deliver an isolated
launch command for the new bundle, with an explicit bridge-off comparison and
recorded version/hash. Do not edit external PowerShell/bash/Codex configuration
or reactivate startup integration without Matt's authorization for that rollout.

The existing installer disable action affects future shells, not already-running
helpers. Correct documentation to make that limit explicit. A live disable path
must revoke the shared admission record, latch the supervisor disabled, and
perform helper shutdown in the background; killing a helper alone is not disable.
Test this state transition before exposing any live toggle. Connecting the tray
checkbox to all sessions is separate product work, not part of this repair.

Rollback is to launch the stock CLI or the bridge-off candidate and retain the
prior bundle. Do not delete user sessions or rewrite conversation state.
Completion requires the stated evidence; missing native build/live evidence is
reported as incomplete validation, not replaced with a passing protocol test.

## Implementation evidence (2026-10-21)

The one-round Fable review remains the only advisory review. The repair moves
startup, pipe waits, watchdogs and teardown into a background supervisor with
one active observation and one replaceable pending observation. Native input
revokes a shared atomic admission word directly; queued older snapshots cannot
restore it. Ordinary typing no longer discards the v2 layout qualification, and
maintenance ticks publish current state without forcing a redraw. Legacy Claude
continues using v1.

The helper waits for its private v2 handshake before installing hooks. The parent
assigns its kill-on-close Job before sending that handshake; this replaces the
plan's suspended-process implementation while preserving hook-start ordering.
The abrupt-parent-exit process test confirms that the helper does not survive.
No live cross-process disable toggle was added. Session `-Disable` now accurately
restores the environment for future children instead of killing helpers that the
supervisor could restart. Restart an existing session bridge-off to disable it.

Measured evidence, retained under `artifacts/`:

- The original synchronous regression waited 1.015 seconds. After the repair,
  1,000 publications plus disable/drop took 0.34 ms in the final fault-test run.
  This measures publication, not native event-to-render latency.
- .NET Release: 255 tests passed (158 Core, 97 App). Standalone Rust: 8 unit
  tests and 11 process tests passed; the normally ignored real .NET/Rust ABI
  test was explicitly run and passed. Five installer and 14 Node tests passed.
- The actual patched Codex CLI builds. Nine Clici-related native TUI tests pass;
  this is a targeted run, not all 5,648 upstream TUI tests. The canonical patch
  reverse-checks against the built checkout. Formatting and diff checks pass.
- Read-only UIA mapping: ten samples each at 112, 1,000 and 3,916 rows all found
  the three correct prompt identities. Median / maximum scan times were
  85.8 / 130.4, 130.0 / 160.4 and 158.1 / 199.9 ms respectively. Narrow-window
  reflow retained all identities. The old long scan exceeded its one-second
  deadline; that measured failure justified changing traversal.
- Real Windows hooks in portable Terminal: each history size passed 40
  navigation actions plus a selection-preservation action. Each delivered all
  100 ordinary characters with zero extra full scans, and passed ineligible
  punctuation through. Evidence is `latency-live-short/evidence/input-live-v2.json`
  and `latency-final-live/{medium,long-clean}/evidence/input-live-v2.json`.
- The actual Codex candidate, with an isolated home and a local HTTP response
  fixture, passed both shortcut directions, return-to-live, distinct prompt IDs,
  unsent draft and nonempty selection preservation without another submission.
  `latency-native-live/evidence/native-input-live.json` records the results.
- Actual Codex typing: 100 marked characters measured from SendInput until UIA
  exposed the updated draft: p50 46.08 ms, p95 47.26 ms, maximum 61.73 ms. This
  includes polling overhead and is not a direct instrumented frame-flush time.
  Raw samples: `latency-native-live/evidence/typing-latency.json`.

Failed fixture attempts are retained, not counted as passing samples. A focus
change refused navigation. Reusing a terminal without clearing its history
exceeded 4,096 rows and correctly refused; the clean long fixture then passed.
The native resume-directory prompt required choosing the isolated directory.

The live-test bundle is `artifacts/latency-candidate-20261021`. The final package
is `artifacts/latency-candidate-final-20261021`; its later changes are the terminal
supervisor diagnostic, a proof-only timing command, and documentation. Its CLI and helper
were built locally. Building the unchanged Code Mode host was prevented by the
pinned V8 150.4.0 release download returning 404. The bundle therefore reuses the
existing host from the same pinned upstream commit, after verifying SHA-256
`9F865A748C984492CA9B69AAC75680E4954F31311C43EC33DDBDAE0ED77BD4BD` against the
installed manifest. A local-provider `codex exec` smoke run with this host passed.
The prior installed bundle is retained for rollback.

Matt confirmed physical typing and both navigation shortcuts work without
changing the draft. The real retry-budget soak passed after 182.04 seconds:
ten replacements exhausted the budget, and ten subsequent publications could
not restart it. The final helper accepted legacy v1; the new native supervisor
refused the installed old helper before sending observations.

Matt explicitly authorized installation and startup re-enablement. The installed
release is `%LOCALAPPDATA%/Clici/renderer-navigation/releases/`
`codex-0.162.1-96aba4ccdd0670bf`. All twenty manifest files verified. A fresh
portable-Terminal PowerShell and fresh interactive Git Bash selected the final
CLI/helper, enabled keyboard navigation and Claude anchors, and the Codex
classic-scrollback setting verified. Existing chats were not closed or rewritten.
Existing Codex processes need restarting to use the repair.

Broader qualification remains open: IME and the full three-agent/focus-signal
suppression matrix; a matched bridge-off/old/new native frame-flush and idle-CPU
comparison. Hook wall
clock measurements include downstream `CallNextHookEx` time and occasionally
exceed the 1 ms p95 target; they must not be represented as isolated Clici CPU
cost. These checks are distinct from the passing typing-latency and navigation
results above. These broader checks are not claimed by this latency repair.

## Recurrence investigation (2026-10-22)

The installed Codex repair was present, but two legacy Claude frame helpers
were also running. With the same Terminal window focused throughout, three
four-second samples measured 2.203 CPU seconds normally, 0.125 with only the
Claude scanning threads temporarily suspended, and 2.484 after resuming them.
Both threads were restored. This establishes scanner-induced Terminal load;
it does not measure typing latency or establish the cause of the complete
freeze. Matt reported recovery after closing Antigravity; that association
has not been independently reproduced.

Legacy helpers bypassed the v2 background-pane check and discovery throttle,
and treated ordinary typing as layout invalidation. The follow-up patch applies
those protections to both transports. Cached legacy qualification now checks
the painted witness too, preserving the identity check when retaining a layout.

The new legacy-hook regression failed before the change and passed afterward.
All 256 .NET tests, 14 Node tests, five installer tests, eight Rust unit tests,
11 process tests, and the explicit Rust/.NET ABI test passed. Whitespace and
diff checks passed. The live input script now checks 100-character typing for
Claude frame transport as well as native v2.

The candidate helper and integrity-checked 20-file bundle are under
`artifacts/typing-recurrence-20261022/`. The native Codex binary is unchanged.
Live typing/navigation verification could not acquire the disposable fixture's
keyboard focus: Windows refused programmatic foreground activation, so the
test timed out without sending input. This gap remains explicit; the patch is
not claimed to establish the cause or resolution of the complete freeze.

Matt subsequently authorized installation. The installed release is
`codex-0.162.1-daea1af5dcc6e6d0`; all twenty manifest files verified. Both
startup profiles point to it, their unrelated content was preserved, and
the Codex configuration is semantically unchanged. A newly launched session
was observed running the new helper.

To update existing sessions without closing conversations, the helper directory
in the prior release (`codex-0.162.1-96aba4ccdd0670bf`) was backed up and replaced,
and its manifest updated to match. This is an explicitly recorded compatibility
update to that prior release, not an immutable copy of its original bundle.
Only the three remaining navigation helper processes were restarted; all three
owning CLI process identities and start times were unchanged. All twenty files
in the compatibility release also verified afterward. Originals and the prior
manifest are preserved under `%LOCALAPPDATA%/Clici/renderer-navigation/backups/`
`typing-recurrence-20261022-225902`; the original bundle also remains in the
repository's ignored artifacts. Installation and process evidence are in
`artifacts/typing-recurrence-20261022/installation-result.json` and
`live-helper-refresh.json`.

## Navigation regression investigation (2026-10-24)

Matt reported that jumping to the latest user turn no longer works. The installed
integration remains enabled and all twenty files in the October 22 bundle pass
their manifest hashes. The observed interactive Codex process instead runs the
stock npm executable and has no renderer helper. Its parent shell was opened by
Dias. Separately, Dias resolves an explicit native executable before opening the
shell and passes that path to its launcher; shell PATH activation therefore does
not select the Clici bundle for that launch. The inspected kickoff selects the
stock Codex desktop binary. This establishes a launch mismatch, not which session
Matt observed failing. That clarification is still outstanding. No installed
files, launch settings, or renderer safeguards were changed.

The live input harness now starts its inspection subprocesses without console
windows. Without that flag, a console-free runner can steal the fixture's focus
while checking it. The modified harness reached helper qualification, but a focus
event revoked the lease before the first shortcut; a fresh fixture could not keep
foreground focus. These attempts are failures, not navigation acceptance. Read-only
mapping in both fixtures found all three distinct prompt identities at 112 rows.
Raw evidence is under `artifacts/navigation-recheck-20261024/`.

The automated baseline passes: 256 .NET tests, fourteen Node tests, five installer
tests, eight Rust unit tests, eleven Rust process tests, and the explicit Rust/.NET
admission ABI test against the installed helper. The native Claude plugin test
requires its own runner; invoking its TypeScript file directly with Node fails
because the `claude-code` module is unavailable there. No native renderer change
was made, and neither restored navigation nor a new live performance result is
claimed. Next, identify the failing launch path and obtain stable fixture focus;
changes to Dias or installed profiles require authorization for those locations.

### Both clients fail: scan deadline reproduced

Matt subsequently confirmed failure in both Codex and Claude. Fresh process
inspection found two Codex sessions using the installed patched executable and
two Claude helpers. One Claude frame contained three prompts and current composer
eligibility. The stock-executable finding therefore does not explain the whole
report.

A one-shot diagnostic using the existing bounded mapper exceeded its one-second
deadline in all four working Terminal windows. Their text captures took about
1–3 ms; traversal, rather than the initial text capture, exhausted the budget.
The windows contained approximately 700–1,300 logical lines, including many
indented and blank lines. These measurements do not prove that every failed
shortcut has the same cause, but establish a reproducible shared failure path.

The `anchor-fixture <directory> 328 dense` regression has 1,000 physical rows,
three known prompt identities, indented replies, and blank lines. The original
mapper times out on it. A uniform-color pruning candidate mapped it in 176.5 ms,
but still timed out on the working windows. A second approach using provider
text searches encountered unusable COM ranges for whitespace searches, including
with the existing bounded-presence guard. Windows Terminal also reports color
attribute searches as unimplemented. No candidate was installed. The scanner was
restored to the committed version; the failed patch and raw results remain under
`artifacts/navigation-diagnosis-20261024/`.

Matt approved the shared scanner rewrite. It keeps the one-second request bound,
4,096-row and one-MiB capture limits, native identity checks, six-cell anchor
verification, selection checks, discovery throttle, and typing invalidation.
The traversal now retains provider ranges instead of recreating them from the
document origin. Textual blank-row offsets are used only when their row count
agrees with UIA's physical line count; ranges with soft wraps are subdivided.
Small uniform-color ranges reject many unrelated blank rows in one call.

The new mapper found all three prompt identities in sparse (112 rows, 72 ms),
dense (1,000 rows, 103 ms), and long dense (3,916 rows, 285 ms) fixtures. The
preceding candidate also preserved coordinates after narrow and wide reflow.
One-shot traversal in four working terminals took 74–597 ms at 1,268–2,950 rows.
Those real-window checks used a different session's bindings to measure traversal;
they do not establish native prompt ownership or successful navigation there.

The first focused keyboard run verified six jumps, then a request expired or
lost its input lease. It is retained as a failed run. After adding small-range
color pruning, both Codex v2 shared admission and Claude frame-file transport
passed forty navigation actions plus the selected-text jump. Each preserved
selection and terminal text, delivered 100 ordinary characters with zero added
full scans, and passed the ineligible shortcut through. Neither run refused a
request. Navigation p95/max was 175/234 ms for Codex and 168/181 ms for Claude.
Hook-chain p95 was 8.6/10.0 ms, with 94/55 ms maximum outliers; these include other
installed hooks and are not an isolated measurement of Clici or physical typing.
The harness requests pane focus explicitly but never relaxes foreground checks.

The twenty-file candidate is under
`artifacts/navigation-diagnosis-20261024/scanner-bundle`. Only the three helper
binaries differ from the installed bundle; the native CLI is unchanged.
Installation and running-helper refresh require Matt's approval for writes
outside this repository. The original installed bundle remains the rollback
source until that update; its helper directory and manifest must be archived
before any compatibility refresh for existing CLI processes.

Eight new row-alignment cases pass, including CRLF endings, missing soft-wrap
boundaries, indented output, short blanks, and Unicode text. All 264 .NET tests,
14 Node tests, five installer tests, and the explicit Rust/.NET admission ABI
check passed. Original native Rust test evidence remains applicable because no
native source changed. No candidate has been installed.

The Taskmaster subagent registered `clici/workspace`; both Codex and Claude
handoffs report saved. Taskmaster's native configuration validation passed.
Its host configuration change has no Lugos Audit receipt because that CLI was
unavailable on the host; the Clici repository receipt is separate.
