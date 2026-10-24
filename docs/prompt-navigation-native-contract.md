# Native prompt identity and keyboard bridge

**2026-10-16: the Codex renderer bridge now has an experimental pre-Terminal
keyboard adapter with fresh native confirmation. It is opt-in and single-pane;
the full qualification matrix remains unfinished.**
Keep the operator's existing `claude` and `codex` commands and classic renderer.

**2026-10-17 Claude integration:** the morning's repeated-prompt failure was a
fixture artifact (a 0 ms fake reply); with a 500 ms reply the stock,
pass-through and anchor cases all retain every prompt. The Claude 2.1.296 plugin
now publishes anchor bindings and composer ownership to the same helper, lease
and keyboard adapter as Codex through a frame file, and the renderer bundle
installer loads it for new shells. The [qualification report](claude-renderer-qualification.md)
has the evidence. Physical Windows Terminal keyboard qualification for Claude
and the composer-only versus whole-pane capture decision remain open.

The [session activation runbook](../tools/proof/prompt-navigation/codex-renderer/README.md)
describes the pinned full Codex CLI bundle, scope checks, immediate disable,
and remaining limitations. Building Clici does not install or activate it.

## Implemented contract

`NavigationIdentity` binds a terminal pane, native agent/session ID, session
epoch and layout epoch. Each `PromptOccurrence` additionally requires an immutable
native message ID. Text and text hashes are not IDs. Duplicate IDs at different
positions make the observation ambiguous and are refused. A cursor needs both
the same native identity and freshly verified position/context evidence; a UUID
alone never authorizes scrolling.

`NavigationInputQueue` defaults to Alt+Shift+Comma (previous) and
Alt+Shift+Period (next), and starts disabled. On a US keyboard these are Alt+<
and Alt+>. Ctrl+Up/Down pass through. Its adapter
must publish a **positively eligible, foreground, focused pane** with an
eligibility revision and a monotonic expiry. Unknown contexts pass through.
The queue itself does not establish eligibility or register Windows input handlers.
`RendererNavigationSession` additionally requires a native observation made after
the intercepted press. `RendererKeyboardHook` supplies input events; the private
renderer bridge supplies short leases and checks the actual pane and anchors.

Each physical press admits at most one request; held repeats are consumed only
while the same press and context remain owned. Releasing Alt or Shift before
the punctuation key does not leak repeats into the draft or create another jump.
A press that began outside the scope stays outside until release. Focus loss,
lifecycle/layout changes, manual
scroll, disable and expired eligibility cancel pending and in-flight admissions.
The remainder of an old held press passes through after a scope change. Input is
never replayed. Initial presses with extra modifiers, injected events and unrelated
keys pass through. Key-up is consumed only for a press still owned by this context.

The default capacity is eight requests including the active one. Distinct presses
retain order; overflow is consumed and returned as `QueueFull` for status reporting.
Only one request may be active. The default and maximum request deadline is one
second, further bounded by the eligibility lease. An in-flight timeout withdraws
the context; a fresh qualified observation is needed before another admission.
An expired queued request is dropped before execution. Passive lease renewal
does not extend an admission. A native confirmation observed after the press
may replace its pre-press lease only while the original admission remains live
and its identity and eligibility revision still match. Confirmed work expires
at the earlier of the confirming lease and one second after the original press.
Confirmation cannot revive expired, invalidated, or differently scoped work.

`CanExecute` must be checked immediately before the scroll and before accepting
its readback. `Complete` only acknowledges queue work; it does not confirm that a
viewport moved. The caller commits the navigation cursor only after a successful
readback. This cannot undo a COM call already started, cancel a hung provider, or
authorize replacement COM workers. Those responsibilities remain in the adapter.

`HotkeyParser.TryParseNavigation` adds modified arrows for this future adapter.
The existing join-hotkey parser retains its grammar. The experimental helper
installs its low-level hook only when explicitly enabled; no tray navigation
setting or persistent keyboard service is installed.
An adapter must report key-up/repeat events and account for keys already held at
startup; a low-level Windows hook does not provide a repeat flag by itself.

## Qualified metadata and remaining vendor work

| Surface | Established evidence | Missing capability |
| --- | --- | --- |
| Claude 2.1.296 function hooks | `session.append` pins the stored UUID, origin, role and main/subagent loop. `ui.render` for `UserMessage` wraps the drawing in blank RGB anchor rows that the COM resolver reads back; a render request is still not a paint, the helper verifies painted rows. The plugin writes frames (session, epoch, bindings, `composerOwnsInput`, wall-clock `observedAt`) to a private file that `renderer-bridge-frames` converts into observe requests with a 750 ms lease. | A read-only composer-focus observation. Ownership is inferred from hook events (anchors active, classic viewport, no dialog awaiting a `tool.check` verdict, no slash-command panel) and withdrawn on any doubt. |
| Claude function-hook input | `ui.press` addresses plugin Buttons/links; `ui.input` addresses plugin Inputs. `ClientSurface.onKey` requires focus in the plugin's region. Keys are therefore taken by Clici's pre-Terminal adapter, as for Codex, never by the plugin. | Nothing from the vendor while capture stays composer-only. Whole-pane capture, including engine dialogs, was never approved and is not implemented. |
| Codex 0.162.1 protocol | [`UserMessageItem.id`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/protocol/src/items.rs) is a native per-item identity; `client_id` correlates submitted input with receipts. The pinned development patch carries these through live/replay history construction and publishes blank-row anchor metadata. | Production qualification of replay, steering, redraw, eviction and pane ownership. A hook's `turn_id` does not substitute for an individual user-message ID. |
| Codex TUI | [`on_committed_user_message` and `on_user_message_display`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/tui/src/chatwidget.rs) construct user history cells and handle steering/replay. [`app/input.rs`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/tui/src/app/input.rs) dispatches input. | A separate read-only navigation action. Existing [backtracking](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/tui/src/app_backtrack.rs) retains rewind semantics and is not an adapter. |

Claude's current local declaration and the real plugin test runtime are the
authority for its early-access function API; ordinary lifecycle-hook docs do not
describe all of it. [Codex lifecycle hooks](https://learn.chatgpt.com/docs/hooks)
provide session/turn metadata, but not this renderer/input contract. No reliable
plugin-only bridge for both classic renderers has been established.

## Required renderer extension

A concrete bridge needs all of the following before it can enable navigation:

1. **Bind the actual input owner.** Publish the local agent process incarnation,
   native session and a Terminal pane binding that can be checked while the pane
   is focused. Renew only while the normal agent input surface owns the keys.
   Withdraw immediately for exit, dialogs, session switch and renderer changes;
   a stale transcript or focus event alone is insufficient.
2. **Publish the rendered occurrence.** At the point the complete user message
   enters retained scrollback, bind its immutable native message ID to a terminal
   anchor. Carry that identity through replay, steering, reflow and redraw. A
   submission-time hook, text search, or guessed row counter cannot supply it.
   Deliberately emitted marks need qualification for placement, duplicate text,
   resize and eviction before they can replace COM occurrence resolution.
3. **Add a read-only input action.** Handle exact configured chords while the
   normal composer owns input, including nonempty drafts and running turns.
   Reject dialogs/pickers and unrelated contexts before consuming the key. Send
   only Previous/Next plus the current binding/eligibility revision; never invoke
   history recall, backtrack, rewind, submit or interrupt. Preserve the draft and
   selection, and keep shell key behavior after exit.
4. **Verify the physical result.** Resolve the current anchor in the focused
   pane, recheck the binding, scroll, then read back before committing a cursor.
   Distinguish own-scroll notifications from manual input. Return to live and
   prove follow with passive output, without input that could cause snap-on-input.

For Codex these changes belong in its protocol-to-history projection and input
dispatch, with a distinct navigation state separate from `app_backtrack`. For
Claude the frame transport and the external keyboard adapter supply them; only
composer focus stays an inference, and the plugin withdraws rather than guesses
when a dialog or panel may hold the keyboard.

The [Codex renderer experiment](../tools/proof/prompt-navigation/codex-renderer/README.md)
implements the identity-to-paint portion in a separate pinned source build.
Two six-cell RGB signatures in existing blank separator rows bind a rendered
occurrence to its native item ID. `RenderedPromptMap` requires complete, unique,
ordered pairs and treats an evicted prefix as a capture boundary. Metadata is
carried over private pipes; the helper verifies actual COM attributes and the
viewport before accepting a jump. Its process and request bounds are explicit.

This does not establish shortcut eligibility. Terminal's
[`ControlCore::TrySendKeyEvent`](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalControl/ControlCore.cpp)
can change selection, and
[`Terminal::SendKeyEvent`](https://github.com/microsoft/terminal/blob/v1.24.12741.0/src/cascadia/TerminalCore/Terminal.cpp)
can snap to live output before the native handler runs. The source finding and
live snap test rule out a native-only shortcut under ordinary defaults.
`CLICI_NATIVE_KEY_PROBE=1` is therefore a separate diagnostic; passive renderer
publication is the default. The opt-in pre-Terminal adapter now adds fresh
eligibility confirmation, pane checks and event-based invalidation. Recovery
after Windows silently removes a hook still requires manual qualification.
Neither the saved metadata report nor its `composerOwnsInput` field is a lease.

Maintaining the experimental Codex renderer build requires replay/steering
identity tests, input-context tests, and a repeated classic Windows Terminal
matrix on upgrades. The Claude plugin is not version-pinned (tested on 2.1.296);
a Claude upgrade that breaks it fails closed and gets fixed then. No vendor binary has been replaced or fork
installed; switching to fullscreen or adding launch wrappers has not been
accepted as a substitute.
