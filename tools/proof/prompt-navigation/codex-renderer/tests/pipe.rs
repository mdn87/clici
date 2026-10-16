use clici_renderer_bridge_proof::{Client, Request};
use std::path::Path;
use std::time::{Duration, Instant};

fn request(session: &str) -> Request<'_> {
    Request {
        version: 1,
        sequence: 7,
        deadline: 750,
        input_observed_at: 0,
        process_id: std::process::id(),
        session_id: session,
        epoch: 1,
        direction: "observe",
        history_complete: true,
        composer_owns_input: false,
        prompts: &[],
    }
}

#[test]
fn private_pipe_round_trip_preserves_sequence_and_pass_through() {
    let mut client = Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
    let reply = client.navigate(&request("valid")).unwrap().unwrap();
    assert_eq!(reply.sequence, 7);
    assert_eq!(reply.action, "Observed");
    assert!(!reply.handled);
}

#[test]
fn stale_reply_disables_the_worker_without_retrying_input() {
    let mut client = Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
    assert!(client.navigate(&request("stale")).unwrap().is_err());
    assert!(client.navigate(&request("valid")).is_none());
}

#[test]
fn hung_worker_is_stopped_within_the_deadline() {
    let mut client = Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
    let start = Instant::now();
    assert!(client.navigate(&request("hang")).unwrap().is_err());
    assert!(start.elapsed() < Duration::from_secs(3));
    assert!(client.navigate(&request("valid")).is_none());
}

#[test]
fn oversized_reply_refuses_and_disables_the_worker() {
    let mut client = Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
    assert!(client.navigate(&request("oversized")).unwrap().is_err());
    assert!(client.navigate(&request("valid")).is_none());
}
