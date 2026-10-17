//! Opt-in, private-pipe renderer protocol. No prompt text crosses this bridge.
use serde::Deserialize;
use serde::Serialize;
use std::collections::HashMap;
use std::io::BufRead;
use std::io::BufReader;
use std::io::Write;
use std::process::Child;
use std::process::Command;
use std::process::Stdio;
use std::sync::mpsc;
use std::time::Duration;

#[derive(Clone, Debug)]
pub struct Anchor {
    pub nonce: String,
    pub item_id: Option<String>,
    pub client_id: Option<String>,
}

impl Anchor {
    pub fn new(item_id: Option<&str>, client_id: Option<&str>) -> Self {
        Self {
            nonce: uuid::Uuid::new_v4().simple().to_string().to_uppercase(),
            item_id: item_id.map(str::to_owned),
            client_id: client_id.map(str::to_owned),
        }
    }

    pub fn colors(&self, end: bool) -> [(u8, u8, u8); 6] {
        let tail = if end { "C3D4" } else { "A1B2" };
        let hex = format!("{}{tail}", self.nonce);
        std::array::from_fn(|index| {
            let byte = |offset| {
                u8::from_str_radix(&hex[index * 6 + offset..index * 6 + offset + 2], 16)
                    .unwrap_or(0)
            };
            (byte(0), byte(2), byte(4))
        })
    }
}

#[derive(Clone, Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Binding {
    pub message_id: String,
    pub nonce: String,
}

#[derive(Default)]
pub struct Receipts {
    ids: HashMap<(String, String), String>,
    failed: bool,
}

impl Receipts {
    pub fn record(&mut self, session: &str, client: &str, item: &str) {
        if !valid_id(session) || !valid_id(client) || !valid_id(item) {
            self.failed = true;
            return;
        }
        let key = (session.to_owned(), client.to_owned());
        if let Some(previous) = self.ids.get(&key) {
            self.failed |= previous != item;
        } else if self.ids.len() < 4096 {
            self.ids.insert(key, item.to_owned());
        } else {
            self.failed = true;
        }
    }

    pub fn resolve(&self, session: &str, anchors: &[Anchor]) -> Option<Vec<Binding>> {
        if self.failed || !valid_id(session) || anchors.is_empty() || anchors.len() > 4096 {
            return None;
        }
        let mut ids = std::collections::HashSet::new();
        anchors
            .iter()
            .map(|anchor| {
                let id = anchor.item_id.as_ref().or_else(|| {
                    self.ids
                        .get(&(session.to_owned(), anchor.client_id.as_ref()?.clone()))
                })?;
                if !valid_id(id) || !ids.insert(id.clone()) {
                    return None;
                }
                Some(Binding {
                    message_id: id.clone(),
                    nonce: anchor.nonce.clone(),
                })
            })
            .collect()
    }
}

fn valid_id(id: &str) -> bool {
    !id.trim().is_empty() && id.len() <= 512 && !id.chars().any(char::is_control)
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Admission {
    Pass,
    Consume,
    Request,
}

#[derive(Default)]
pub struct KeyGate {
    held: [bool; 2],
    owned: [bool; 2],
}

impl KeyGate {
    pub fn attach(&mut self, held: [bool; 2]) {
        self.held = held;
        self.owned = [false; 2];
    }
    pub fn invalidate(&mut self) {
        self.owned = [false; 2];
    }
    pub fn accept(&mut self, index: usize) {
        self.owned[index] = true;
    }
    pub fn key(
        &mut self,
        index: usize,
        down: bool,
        repeat: bool,
        exact_control: bool,
        eligible: bool,
    ) -> Admission {
        if !eligible {
            self.invalidate();
        }
        if !down {
            self.held[index] = false;
            let owned = std::mem::take(&mut self.owned[index]);
            return if owned && eligible {
                Admission::Consume
            } else {
                Admission::Pass
            };
        }
        if self.held[index] || repeat {
            self.held[index] = true;
            return if self.owned[index] && exact_control && eligible {
                Admission::Consume
            } else {
                Admission::Pass
            };
        }
        self.held[index] = true;
        if exact_control && eligible {
            Admission::Request
        } else {
            Admission::Pass
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Request<'a> {
    pub version: u8,
    pub sequence: u64,
    pub deadline: u64,
    pub input_observed_at: u64,
    pub process_id: u32,
    pub session_id: &'a str,
    pub epoch: u64,
    pub direction: &'a str,
    pub history_complete: bool,
    pub composer_owns_input: bool,
    pub prompts: &'a [Binding],
}

#[derive(Deserialize, Debug)]
pub struct Reply {
    pub sequence: u64,
    pub handled: bool,
    pub action: String,
    #[serde(default)]
    pub reason: String,
}

// A failed helper is replaced, never reused: its channels may still hold a late
// reply. The delay doubles per consecutive failure so a broken helper cannot
// stall every frame.
pub const MAX_RESTARTS: u32 = 10;

pub fn restart_delay(failures: u32) -> Duration {
    Duration::from_millis(1000u64 << failures.clamp(1, 6).saturating_sub(1)).min(Duration::from_secs(30))
}

// One worker owns all pipe I/O. If a provider or pipe stalls, the native input
// thread times out and stops its child; it never blocks on write_all/read_line.
pub struct Client {
    child: Child,
    requests: mpsc::SyncSender<String>,
    replies: mpsc::Receiver<Result<String, String>>,
    failed: bool,
}

impl Client {
    pub fn is_available(&self) -> bool {
        !self.failed
    }

    pub fn start(executable: &std::path::Path) -> Result<Self, String> {
        if !executable.is_absolute() {
            return Err("bridge executable must be an absolute path".into());
        }
        let mut command = Command::new(executable);
        command
            .args(["renderer-bridge", &std::process::id().to_string()])
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000); // CREATE_NO_WINDOW
        }
        let mut child = command.spawn().map_err(|error| error.to_string())?;
        let mut input = child.stdin.take().ok_or("missing bridge stdin")?;
        let output = child.stdout.take().ok_or("missing bridge stdout")?;
        let (requests, incoming) = mpsc::sync_channel::<String>(1);
        let (outgoing, replies) = mpsc::sync_channel(1);
        std::thread::spawn(move || {
            let mut output = BufReader::new(output);
            let read = |output: &mut BufReader<_>| -> Result<String, String> {
                let mut bytes = Vec::new();
                // take() bounds an untrusted/malformed worker reply before allocation.
                std::io::Read::take(output, 4097)
                    .read_until(b'\n', &mut bytes)
                    .map_err(|error| error.to_string())?;
                if bytes.len() > 4096 || bytes.last() != Some(&b'\n') {
                    return Err("invalid bridge reply".into());
                }
                String::from_utf8(bytes).map_err(|error| error.to_string())
            };
            if outgoing.send(read(&mut output)).is_err() {
                return;
            }
            while let Ok(line) = incoming.recv() {
                let reply = input
                    .write_all(line.as_bytes())
                    .and_then(|_| input.write_all(b"\n"))
                    .and_then(|_| input.flush())
                    .map_err(|error| error.to_string())
                    .and_then(|_| read(&mut output));
                if outgoing.send(reply).is_err() {
                    return;
                }
            }
        });
        let mut client = Self {
            child,
            requests,
            replies,
            failed: false,
        };
        let hello = client
            .replies
            .recv_timeout(Duration::from_millis(1000))
            .map_err(|error| error.to_string())
            .and_then(|reply| reply)
            .and_then(|line| {
                serde_json::from_str::<serde_json::Value>(&line).map_err(|error| error.to_string())
            });
        if !hello.is_ok_and(|hello| hello["version"] == 1 && hello["ready"] == true) {
            client.stop();
            return Err("bridge did not become ready".into());
        }
        Ok(client)
    }

    // None means no request was sent: normal input may continue. An error after
    // dispatch is handled, because scrolling may already have happened.
    pub fn navigate(&mut self, request: &Request<'_>) -> Option<Result<Reply, String>> {
        if self.failed {
            return None;
        }
        let line = serde_json::to_string(request).ok()?;
        if line.len() > 1_048_576 || self.requests.try_send(line).is_err() {
            return None;
        }
        let reply = self
            .replies
            .recv_timeout(Duration::from_millis(1000))
            .map_err(|error| error.to_string())
            .and_then(|reply| reply)
            .and_then(|line| {
                serde_json::from_str::<Reply>(&line).map_err(|error| error.to_string())
            })
            .and_then(|reply| {
                if reply.sequence == request.sequence {
                    Ok(reply)
                } else {
                    Err("stale bridge reply".into())
                }
            });
        if reply.is_err() {
            self.stop();
        }
        Some(reply)
    }

    fn stop(&mut self) {
        self.failed = true;
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

impl Drop for Client {
    fn drop(&mut self) {
        self.stop();
    }
}

#[cfg(windows)]
pub fn monotonic_milliseconds() -> u64 {
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn GetTickCount64() -> u64;
    }
    unsafe { GetTickCount64() }
}

#[cfg(not(windows))]
pub fn monotonic_milliseconds() -> u64 {
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn receipt_joins_optimistic_paint_to_native_id_without_text_matching() {
        let mut receipts = Receipts::default();
        let anchor = Anchor::new(None, Some("client"));
        assert!(
            receipts
                .resolve("session", std::slice::from_ref(&anchor))
                .is_none()
        );
        receipts.record("session", "client", "item-1");
        assert_eq!(
            receipts
                .resolve("session", std::slice::from_ref(&anchor))
                .unwrap()[0]
                .message_id,
            "item-1"
        );
        assert!(receipts.resolve("other-session", &[anchor]).is_none());
    }

    #[test]
    fn replay_and_same_turn_steers_retain_distinct_native_ids() {
        let receipts = Receipts::default();
        let anchors = [
            Anchor::new(Some("item-1"), None),
            Anchor::new(Some("item-2"), None),
        ];
        let resolved = receipts.resolve("session", &anchors).unwrap();
        assert_eq!(resolved.len(), 2);
        assert_ne!(resolved[0].nonce, resolved[1].nonce);
        assert!(
            receipts
                .resolve("session", &[anchors[0].clone(), anchors[0].clone()])
                .is_none()
        );
    }

    #[test]
    fn conflicting_receipts_withdraw_instead_of_reassigning_an_anchor() {
        let mut receipts = Receipts::default();
        receipts.record("session", "client", "one");
        receipts.record("session", "client", "two");
        assert!(
            receipts
                .resolve("session", &[Anchor::new(None, Some("client"))])
                .is_none()
        );
    }

    #[test]
    fn wire_colors_agree_with_terminal_rgb_order_and_use_distinct_end_tags() {
        let anchor = Anchor {
            nonce: "0123456789ABCDEF0123456789ABCDEF".into(),
            item_id: None,
            client_id: None,
        };
        assert_eq!(
            anchor.colors(false),
            [
                (1, 35, 69),
                (103, 137, 171),
                (205, 239, 1),
                (35, 69, 103),
                (137, 171, 205),
                (239, 161, 178)
            ]
        );
        assert_ne!(anchor.colors(false), anchor.colors(true));
    }

    #[test]
    fn one_action_per_press_even_when_windows_reports_repeat_as_press() {
        let mut gate = KeyGate::default();
        assert_eq!(gate.key(0, true, false, true, true), Admission::Request);
        gate.accept(0);
        assert_eq!(gate.key(0, true, false, true, true), Admission::Consume);
        assert_eq!(gate.key(0, false, false, false, true), Admission::Consume);
        assert_eq!(gate.key(0, true, false, true, true), Admission::Request);
    }

    #[test]
    fn modal_focus_loss_and_keys_held_on_entry_do_not_acquire_a_press() {
        let mut gate = KeyGate::default();
        gate.attach([true, false]);
        assert_eq!(gate.key(0, true, false, true, true), Admission::Pass);
        gate.key(0, false, false, true, true);
        assert_eq!(gate.key(0, true, false, true, false), Admission::Pass);
        assert_eq!(gate.key(0, true, false, true, true), Admission::Pass);
        gate.key(0, false, false, true, true);
        assert_eq!(gate.key(0, true, false, true, true), Admission::Request);
        gate.accept(0);
        gate.invalidate();
        assert_eq!(gate.key(0, false, false, true, true), Admission::Pass);
    }

    #[test]
    fn restart_delay_doubles_and_is_capped() {
        assert_eq!(restart_delay(1), Duration::from_secs(1));
        assert_eq!(restart_delay(2), Duration::from_secs(2));
        assert_eq!(restart_delay(5), Duration::from_secs(16));
        assert_eq!(restart_delay(6), Duration::from_secs(30));
        assert_eq!(restart_delay(40), Duration::from_secs(30));
    }

    #[test]
    fn extra_modifiers_and_unowned_repeat_are_passed_through() {
        let mut gate = KeyGate::default();
        assert_eq!(gate.key(1, true, false, false, true), Admission::Pass);
        assert_eq!(gate.key(1, true, false, true, true), Admission::Pass);
        gate.key(1, false, false, true, true);
        assert_eq!(gate.key(1, true, true, true, true), Admission::Pass);
    }
}
