//! Native Codex integration, copied into the pinned development checkout.
#[path = "clici_bridge_protocol.rs"]
mod protocol;
use crate::history_cell::HistoryCell;
use crate::history_cell::UserHistoryCell;
use crate::terminal_hyperlinks::HyperlinkLine;
use crate::tui::TuiEvent;
use crossterm::event::{KeyCode, KeyEventKind, KeyModifiers};
use protocol::Admission;
pub(crate) use protocol::Anchor;
use protocol::Client;
use protocol::KeyGate;
use protocol::Receipts;
use protocol::Request;
use ratatui::style::{Color, Style};
use ratatui::text::{Line, Span};
use std::sync::{Arc, Mutex, OnceLock};

#[derive(Default)]
struct State {
    receipts: Receipts,
    keys: KeyGate,
    client: Option<Client>,
    attempted: bool,
    epoch: u64,
    sequence: u64,
    session: Option<String>,
    last_frame: Option<(String, u64, bool, bool, Vec<protocol::Binding>)>,
}

fn state() -> &'static Mutex<State> {
    static STATE: OnceLock<Mutex<State>> = OnceLock::new();
    STATE.get_or_init(|| {
        let mut state = State::default();
        state.keys.attach(held_arrows());
        Mutex::new(state)
    })
}

fn enabled() -> bool {
    cfg!(windows)
        && std::env::var_os("WT_SESSION").is_some()
        && std::env::var_os("NO_COLOR").is_none()
        && std::env::var_os("CLICI_RENDERER_BRIDGE").is_some()
}

pub(crate) fn keyboard_enabled() -> bool {
    enabled() && std::env::var("CLICI_RENDERER_KEYS").as_deref() == Ok("1")
}

// Capture before the caller examines composer/modal state. Serialization or
// child startup must never make an older input observation appear fresh.
pub(crate) fn observation_time() -> u64 {
    protocol::monotonic_milliseconds()
}

pub(crate) fn poll_keyboard() -> bool {
    keyboard_enabled()
        && state().lock().is_ok_and(|state| state.client.as_ref().is_some_and(Client::is_available))
}

pub(crate) fn anchor(item_id: Option<&str>, client_id: Option<&str>) -> Option<Anchor> {
    if !enabled() {
        return None;
    }
    let _ = state(); // Observe keys held when attaching, before the first key request.
    Some(Anchor::new(item_id, client_id))
}

pub(crate) fn receipt(session: &str, client: Option<&str>, item: &str) {
    if enabled()
        && let Some(client) = client
        && let Ok(mut state) = state().lock()
    {
        state.receipts.record(session, client, item);
    }
}

pub(crate) fn decorate(lines: &mut [HyperlinkLine], anchor: Option<&Anchor>, width: u16) {
    let Some(anchor) = anchor else {
        return;
    };
    if width < 6 || lines.len() < 3 {
        return;
    }
    let last = lines.len() - 1;
    // Only the two existing empty separator rows may carry anchors. Never add
    // characters to prompt text or use the model-visible stored message.
    if lines[0].line.width() != 0 || lines[last].line.width() != 0 {
        return;
    }
    for (index, end) in [(0, false), (last, true)] {
        let style = lines[index].line.style;
        lines[index].line = Line::from(
            anchor
                .colors(end)
                .into_iter()
                .map(|(r, g, b)| Span::styled(" ", Style::default().fg(Color::Rgb(r, g, b))))
                .collect::<Vec<_>>(),
        )
        .style(style);
    }
}

// Preserve only our six-cell blank separator through the whitespace-trimming
// history wrapper. This is a render hint, never evidence of prompt ownership.
pub(crate) fn is_anchor_line(line: &HyperlinkLine, width: usize) -> bool {
    width >= 6
        && line.source.is_none()
        && line.hyperlinks.is_empty()
        && line.line.spans.len() == 6
        && line
            .line
            .spans
            .iter()
            .all(|span| span.content == " " && matches!(span.style.fg, Some(Color::Rgb(_, _, _))))
        && matches!(
            line.line.spans[5].style.fg,
            Some(Color::Rgb(_, 0xA1, 0xB2) | Color::Rgb(_, 0xC3, 0xD4))
        )
}

pub(crate) fn event(
    event: &TuiEvent,
    eligible: bool,
    session: Option<String>,
    cells: &[Arc<dyn HistoryCell>],
    history_complete: bool,
) -> bool {
    if !enabled() {
        return false;
    }
    let Ok(mut state) = state().lock() else {
        return false;
    };
    if state.session != session {
        state.session = session.clone();
        state.epoch += 1;
        state.keys.invalidate();
    }
    match event {
        TuiEvent::FocusLost
        | TuiEvent::Resize(_)
        | TuiEvent::Resume
        | TuiEvent::Mouse(_)
        | TuiEvent::Paste(_) => {
            state.epoch += 1;
            state.keys.invalidate();
            return false;
        }
        TuiEvent::FocusGained => {
            state.epoch += 1;
            state.keys.attach(held_arrows());
            return false;
        }
        _ => {}
    }
    if !eligible {
        state.keys.invalidate();
        // Modal/overlay draws may bypass the normal composer publication path.
        // Withdraw the input lease before handing their input to the application.
        if keyboard_enabled() && state.client.is_some() {
            state.sequence += 1;
            if let Some(session) = session.as_deref() {
                let request = Request {
                    version: 1,
                    sequence: state.sequence,
                    deadline: protocol::monotonic_milliseconds() + 750,
                    input_observed_at: protocol::monotonic_milliseconds(),
                    process_id: std::process::id(),
                    session_id: session,
                    epoch: state.epoch,
                    direction: "observe",
                    history_complete,
                    composer_owns_input: false,
                    prompts: &[],
                };
                if let Some(client) = state.client.as_mut() {
                    let _ = client.navigate(&request);
                }
            }
        }
    }
    if std::env::var("CLICI_NATIVE_KEY_PROBE").as_deref() != Ok("1") {
        return false;
    }
    let TuiEvent::Key(key) = event else {
        return false;
    };
    let index = match key.code {
        KeyCode::Up => 0,
        KeyCode::Down => 1,
        _ => return false,
    };
    match state.keys.key(
        index,
        key.kind != KeyEventKind::Release,
        key.kind == KeyEventKind::Repeat,
        key.modifiers == KeyModifiers::CONTROL,
        eligible,
    ) {
        Admission::Pass => return false,
        Admission::Consume => return true,
        Admission::Request => {}
    }
    let Some(session) = session else {
        return false;
    };
    let anchors: Option<Vec<_>> = cells
        .iter()
        .filter_map(|cell| cell.as_any().downcast_ref::<UserHistoryCell>())
        .map(|cell| cell.clici_anchor.clone())
        .collect();
    let Some(anchors) = anchors else {
        return false;
    };
    let Some(bindings) = state.receipts.resolve(&session, &anchors) else {
        return false;
    };
    if !state.attempted {
        state.attempted = true;
        let Some(path) = std::env::var_os("CLICI_RENDERER_BRIDGE") else {
            return false;
        };
        match Client::start(std::path::Path::new(&path)) {
            Ok(client) => state.client = Some(client),
            Err(error) => {
                tracing::warn!(%error, "Clici renderer bridge unavailable");
                return false;
            }
        }
    }
    state.sequence += 1;
    let request = Request {
        version: 1,
        sequence: state.sequence,
        deadline: protocol::monotonic_milliseconds() + 750,
        input_observed_at: protocol::monotonic_milliseconds(),
        process_id: std::process::id(),
        session_id: &session,
        epoch: state.epoch,
        direction: if index == 0 { "previous" } else { "next" },
        history_complete,
        composer_owns_input: true,
        prompts: &bindings,
    };
    let result = state
        .client
        .as_mut()
        .and_then(|client| client.navigate(&request));
    let handled = match result {
        Some(Ok(reply)) => {
            tracing::debug!(action = reply.action, "Clici navigation result");
            reply.handled
        }
        Some(Err(error)) => {
            tracing::warn!(%error, "Clici navigation result unverified; bridge disabled");
            true
        }
        None => false,
    };
    if handled {
        state.keys.accept(index);
    }
    handled
}

// Called after Codex has flushed the frame. Passive publication never requests
// navigation. Unknown optimistic receipts withdraw the complete prompt map.
pub(crate) fn publish(
    session: Option<String>,
    cells: &[Arc<dyn HistoryCell>],
    eligible: bool,
    history_complete: bool,
    input_observed_at: u64,
) {
    if !enabled() {
        return;
    }
    let Some(session) = session else {
        return;
    };
    let Ok(mut state) = state().lock() else {
        return;
    };
    let anchors: Option<Vec<_>> = cells
        .iter()
        .filter_map(|cell| cell.as_any().downcast_ref::<UserHistoryCell>())
        .map(|cell| cell.clici_anchor.clone())
        .collect();
    let bindings = anchors.and_then(|anchors| state.receipts.resolve(&session, &anchors));
    let eligible = eligible && bindings.is_some();
    let bindings = bindings.unwrap_or_default();
    let frame = (
        session.clone(),
        state.epoch,
        eligible,
        history_complete,
        bindings.clone(),
    );
    if !keyboard_enabled() && state.last_frame.as_ref() == Some(&frame) {
        return;
    }
    state.last_frame = Some(frame);
    if !state.attempted {
        state.attempted = true;
        let Some(path) = std::env::var_os("CLICI_RENDERER_BRIDGE") else {
            return;
        };
        match Client::start(std::path::Path::new(&path)) {
            Ok(client) => state.client = Some(client),
            Err(error) => {
                tracing::warn!(%error, "Clici renderer bridge unavailable");
                return;
            }
        }
    }
    state.sequence += 1;
    let request = Request {
        version: 1,
        sequence: state.sequence,
        deadline: input_observed_at + 750,
        input_observed_at,
        process_id: std::process::id(),
        session_id: &session,
        epoch: state.epoch,
        direction: "observe",
        history_complete,
        composer_owns_input: eligible,
        prompts: &bindings,
    };
    if let Some(Err(error)) = state
        .client
        .as_mut()
        .and_then(|client| client.navigate(&request))
    {
        tracing::warn!(%error, "Clici renderer publication stopped");
    }
}

#[cfg(windows)]
fn held_arrows() -> [bool; 2] {
    #[link(name = "user32")]
    unsafe extern "system" {
        fn GetAsyncKeyState(key: i32) -> i16;
    }
    unsafe { [GetAsyncKeyState(0x26) < 0, GetAsyncKeyState(0x28) < 0] }
}

#[cfg(not(windows))]
fn held_arrows() -> [bool; 2] {
    [false; 2]
}
