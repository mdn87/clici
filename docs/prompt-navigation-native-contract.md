# Native prompt identity and keyboard bridge

**2026-10-16: the identity model, metadata probe and key-admission logic are
implemented; an end-to-end classic-scrollback bridge is not available.**
Keep the operator's existing `claude` and `codex` commands and classic renderer.

## Implemented contract

`NavigationIdentity` binds a terminal pane, native agent/session ID, session
epoch and layout epoch. Each `PromptOccurrence` additionally requires an immutable
native message ID. Text and text hashes are not IDs. Duplicate IDs at different
positions make the observation ambiguous and are refused. A cursor needs both
the same native identity and freshly verified position/context evidence; a UUID
alone never authorizes scrolling.

`NavigationInputQueue` defaults to Ctrl+Up/Down and disabled. A future adapter
must publish a **positively eligible, foreground, focused pane** with an
eligibility revision and a monotonic expiry. Unknown contexts pass through.
The queue does not establish eligibility or register Windows input handlers.

Each physical press admits at most one request; held repeats are consumed only
while the same chord and context remain owned. A press that began outside the
scope stays outside until release. Focus loss, lifecycle/layout changes, manual
scroll, disable and expired eligibility cancel pending and in-flight admissions.
The remainder of an old held press passes through after a scope change. Input is
never replayed. Initial presses with extra modifiers, injected events and unrelated
keys pass through. Key-up is consumed only for a press still owned by this context.

The default capacity is eight requests including the active one. Distinct presses
retain order; overflow is consumed and returned as `QueueFull` for status reporting.
Only one request may be active. The default and maximum request deadline is one
second, further bounded by the eligibility lease. An in-flight timeout withdraws
the context; a fresh qualified observation is needed before another admission.
An expired queued request is dropped before execution. A renewed lease never
extends an already admitted request's deadline.

`CanExecute` must be checked immediately before the scroll and before accepting
its readback. `Complete` only acknowledges queue work; it does not confirm that a
viewport moved. The caller commits the navigation cursor only after a successful
readback. This cannot undo a COM call already started, cancel a hung provider, or
authorize replacement COM workers. Those responsibilities remain in the adapter.

`HotkeyParser.TryParseNavigation` adds modified arrows for this future adapter.
The existing join-hotkey parser retains its grammar. No navigation settings,
global hotkey, low-level hook, or tray service is installed by these changes.
An adapter must report key-up/repeat events and account for keys already held at
startup; a low-level Windows hook does not provide a repeat flag by itself.

## Qualified metadata and remaining vendor work

| Surface | Established evidence | Missing capability |
| --- | --- | --- |
| Claude 2.1.296 function hooks | `session.append` pins the stored UUID, origin, role and main/subagent loop. `UserMessage.requestId` names the render instance. The development collector joins those observations without scanning history. | A classic-mode buffer anchor and a composer-wide read-only key action. A render request is not a successful paint. Render-only observations do not establish main-loop membership. |
| Claude function-hook input | `ui.press` addresses plugin Buttons/links; `ui.input` addresses plugin Inputs. `ClientSurface.onKey` requires focus in the plugin's region. Button actions reuse existing engine actions. | A new scoped action that can receive Ctrl+Up/Down from the composer without borrowing another action or moving focus away from the draft. |
| Codex 0.162.1 protocol | [`UserMessageItem.id`](https://github.com/openai/codex/blob/rust-v0.162.1/codex-rs/protocol/src/items.rs) is a native per-item identity; `client_id` also correlates submitted input with receipts. | Carry the item ID through display/history construction and publish a renderer anchor for it. A hook's `turn_id` does not substitute for an individual user-message ID. |
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
Claude they require an expanded vendor API or a separately qualified external
input/anchor mechanism. Clici cannot supply the missing renderer facts by adding
more confidence checks around the current hooks.

Maintaining a Codex renderer fork would require a pinned build, replay/steering
identity tests, input-context tests, and a repeated classic Windows Terminal
matrix on upgrades. It would still not solve Claude's missing API. No vendor
binary has been replaced or fork installed; switching to fullscreen or adding
launch wrappers has not been accepted as a substitute.
