# Fable review of the typing-latency repair plan

One fresh, read-only review ran before implementation. The review invocation
used the Cross Review skill through Autowork, with no provider substitution or
second round. The plan was revised afterward by the driving Codex agent.

- Run: `20261021-015945-cross-review`
- Result: `succeeded` (review delivery, not a claim that the proposed fix works)
- Route/model: `claude-fable` / `fable`, effort `high`, adversarial mode
- Adapter: `ClaudeFableReviewer/1`; fresh nonpersistent CLI session, tools disabled
- Completed: `2026-10-21T02:05:03Z`
- Reviewed manifest SHA-256: `07dd8c2d57ceb794074fb07b7eabcccef891b86288f34180c469b9fe8a732902`
- Evidence store: `%LOCALAPPDATA%/lugos/autowork/runs/20261021-015945-cross-review/`
- Evidence files: `review-packet.md`, `subject-manifest.json`,
  `reviewer-fingerprint.json`, `findings.jsonl`, `review-stats.json`

The service's preliminary adjudications mark schema-valid findings accepted;
they do not establish technical correctness. The table below records the root
agent's actual disposition. All changes concern the plan; no implementation has
been tested or accepted. The revised plan has not received a second review.

| ID | Reviewer severity | Disposition and resulting change |
| --- | --- | --- |
| F-001 | high | Partly accepted. Simplify the cross-process record, but reject shipping keyboard-enabled async without immediate revocation. A confirmation observed after the key can still precede a later modal transition; processing it late could scroll before lease expiry. Added this counterexample and retained the revocation prerequisite. Async-only benchmarks are permitted. |
| F-002 | high | Accepted simplification: immutable header plus one atomic 64-bit epoch/eligibility word, not a multi-field seqlock. Keep timestamps in the pipe and name the existing shared Windows clock. A resettable event alone was not selected because an old positive message must be distinguishable from a new generation. |
| F-003 | high | Accepted. Define invalidation epoch separately from publication sequence; heartbeats do not revoke valid navigation. Require a successful operation longer than one timer tick and a strict post-key observation timestamp. |
| F-004 | high | Accepted. Explicitly prohibit UIA, I/O, logging, and allocation in hook callbacks; require bounded cached-state operations and callback latency measurements. |
| F-005 | medium | Accepted gap, rejected proposed relaxation. Discovery retry never prolongs a positive lease; use native focus reporting plus helper evidence, test lost events and competing sessions, and leave unsupported contexts unqualified. A cached runtime ID does not solve a missed event. Do not claim instantaneous awareness from a finite lease. |
| F-006 | medium | Accepted documentation gap. Record existing native-mark proof failures and relevant open-source alternatives. Rejected the claim that Terminal marks automatically satisfy agent-specific eligibility; they lack that contract. |
| F-007 | medium | Accepted. Separate the supervisor/watchdog from blocking pipe I/O; specify deadline termination, pipe closure, bounded teardown, and write-stall verification. |
| F-008 | medium | Accepted. Specify a kill-on-close Job, assignment before child hook installation, parent-exit behavior, and no-orphan/exit-latency tests. |
| F-009 | medium | Partly accepted. Add no-lease draft/cursor/selection comparisons and physical observation. Reject ignoring ESC/punctuation in the native TUI: unhandled keys must retain native behavior. Removing the diagnostic Ctrl-arrow path is unrelated to Alt+Shift punctuation encoding. Preservation claims apply to handled navigation, with native pass-through behavior documented. |
| F-010 | medium | Accepted. Gate the revised cache/admission path by protocol version; legacy Claude retains single-revision behavior. Add mixed two-Codex/one-Claude tests and legacy typing/focus checks. |
| F-011 | medium | Accepted. Require at least 9/10 correct jumps in each direction at every specified history size with a healthy helper. Fault refusals and healthy success are separate criteria; scan optimization becomes necessary if it fails this floor. |
| F-012 | low | Accepted. Direct atomic invalidation bypasses the try-lock mailbox. Mailbox contention may drop a rich observation but cannot defer revocation. |
| F-013 | low | Accepted. Specify 10 replacements, capped exponential backoff, a latched diagnostic with recovery instruction, and new Codex process as the explicit reset. |
| F-014 | low | Accepted ownership clarification. Use an unnamed mapping and read-only child handle; test rights. Do not expand the threat model to arbitrary malicious same-user/debug-capable processes. |
| F-015 | low | Accepted. Local pinned source already enables focus reporting (`tui.rs`, `EnableFocusChange`) and maps the focus events. Preserve it and require agreement with helper evidence, rather than assume UIA alone suffices. |
| F-016 | nit | Accepted. Replace ambiguous fail-closed language with no interception/no navigation and keys passing through. |

The next implementation step is the instrumented delayed/hung-helper regression
test, followed by asynchronous supervision and the simplified revocation contract.
Global startup integration remains disabled. Completion still requires native
build, automated behavior checks, and the specified isolated live evidence.
