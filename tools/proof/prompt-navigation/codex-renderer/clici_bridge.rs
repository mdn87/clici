//! Native Codex integration, copied into the pinned development checkout.
#[path = "clici_bridge_protocol.rs"]
mod protocol;
use crate::history_cell::HistoryCell;
use crate::history_cell::UserHistoryCell;
use crate::terminal_hyperlinks::HyperlinkLine;
use crate::tui::TuiEvent;
pub(crate) use protocol::Anchor;
use protocol::AsyncClient;
use protocol::Observation;
use protocol::Receipts;
use ratatui::style::{Color, Style};
use ratatui::text::{Line, Span};
use std::sync::{Arc, Mutex, OnceLock};

#[derive(Default)]
struct State {
    receipts: Receipts,
    client: Option<AsyncClient>,
    start_attempted: bool,
    last_reason: Option<(String, String)>,
    epoch: u64,
    admission_epoch: u64,
    eligible: bool,
    sequence: u64,
    session: Option<String>,
    bindings: Arc<Vec<protocol::Binding>>,
}

fn state() -> &'static Mutex<State> {
    static STATE: OnceLock<Mutex<State>> = OnceLock::new();
    STATE.get_or_init(|| Mutex::new(State::default()))
}

fn revoke(state: &mut State) {
    state.eligible = false;
    state.admission_epoch = state.admission_epoch.saturating_add(1);
    if let Some(client) = &state.client {
        client.invalidate(state.admission_epoch);
    }
}

// Used before async handlers that may change input ownership while awaiting I/O.
pub(crate) fn invalidate() {
    if enabled()
        && let Ok(mut state) = state().lock()
    {
        revoke(&mut state);
    }
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
        && state().lock().is_ok_and(|state| {
            !state.start_attempted || state.client.as_ref().is_some_and(AsyncClient::is_available)
        })
}

fn ensure_client(state: &mut State) -> bool {
    if !state.start_attempted {
        state.start_attempted = true;
        if let Some(path) = std::env::var_os("CLICI_RENDERER_BRIDGE") {
            match AsyncClient::start(path.into()) {
                Ok(client) => state.client = Some(client),
                Err(error) => {
                    tracing::warn!(%error, "Clici bridge disabled; restart Codex to retry")
                }
            }
        }
    }
    state.client.as_ref().is_some_and(AsyncClient::is_available)
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
    _cells: &[Arc<dyn HistoryCell>],
    _history_complete: bool,
) -> bool {
    if !enabled() {
        return false;
    }
    let Ok(mut state) = state().lock() else {
        return false;
    };
    if state.session != session {
        state.session = session;
        state.epoch += 1;
        revoke(&mut state);
    }
    match event {
        TuiEvent::FocusLost
        | TuiEvent::FocusGained
        | TuiEvent::Resize(_)
        | TuiEvent::Resume
        | TuiEvent::Mouse(_) => {
            state.epoch += 1;
            revoke(&mut state);
        }
        TuiEvent::Key(_) | TuiEvent::Paste(_) => revoke(&mut state),
        _ => {}
    }
    if !eligible && state.eligible {
        revoke(&mut state);
    }
    // Production admission is before Terminal. Native Ctrl-arrow probing cannot
    // wait for an asynchronous handled result, so all native input passes through.
    false
}

// Called after a flushed frame or a state-only maintenance tick. Neither path
// waits for the helper, and a tick never refreshes a cached observation's time.
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
    let Ok(mut state) = state().lock() else {
        return;
    };
    let Some(session) = session else {
        revoke(&mut state);
        return;
    };
    if state.session.as_ref() != Some(&session) {
        state.session = Some(session.clone());
        state.epoch += 1;
        revoke(&mut state);
    }
    let anchors: Option<Vec<_>> = cells
        .iter()
        .filter_map(|cell| cell.as_any().downcast_ref::<UserHistoryCell>())
        .map(|cell| cell.clici_anchor.clone())
        .collect();
    let bindings = anchors.and_then(|anchors| state.receipts.resolve(&session, &anchors));
    let eligible = eligible && bindings.is_some();
    let bindings = bindings.unwrap_or_default();
    if *state.bindings != bindings {
        state.bindings = Arc::new(bindings);
        state.epoch += 1;
        revoke(&mut state);
    }
    if !eligible && state.eligible {
        revoke(&mut state);
    }
    state.eligible = eligible;
    if let Some(result) = state.client.as_ref().and_then(AsyncClient::try_result) {
        let reason = match result {
            Ok(reply) => (reply.action, reply.reason),
            Err(error) => ("Unavailable".into(), error),
        };
        if state.last_reason.as_ref() != Some(&reason) {
            tracing::info!(action = %reason.0, reason = %reason.1, "Clici renderer state");
            state.last_reason = Some(reason);
        }
    }
    if !ensure_client(&mut state) {
        return;
    }
    state.sequence += 1;
    let observation = Observation {
        sequence: state.sequence,
        observed_at: input_observed_at,
        session,
        epoch: state.epoch,
        admission_epoch: state.admission_epoch,
        eligible,
        history_complete,
        prompts: state.bindings.clone(),
    };
    if let Some(client) = &state.client {
        client.publish(observation);
    }
}
