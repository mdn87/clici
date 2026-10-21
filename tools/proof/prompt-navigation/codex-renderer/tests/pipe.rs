use clici_renderer_bridge_proof::{
    AsyncClient, Binding, Client, Observation, Request, monotonic_milliseconds,
};
use std::path::Path;
use std::sync::Arc;
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
        admission_word: None,
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
fn publication_does_not_wait_for_a_hung_helper() {
    let client =
        AsyncClient::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture")).to_owned())
            .unwrap();
    assert!(client.publish(observation("hang", 1)));
    std::thread::sleep(Duration::from_millis(200));
    let start = Instant::now();
    for sequence in 2..1002 {
        client.publish(observation("valid", sequence));
    }
    client.disable();
    drop(client);
    let elapsed = start.elapsed();
    eprintln!("native publication duration: {elapsed:?}");
    assert!(
        elapsed < Duration::from_millis(100),
        "helper blocked native publication: {elapsed:?}"
    );
}

fn observation(session: &str, sequence: u64) -> Observation {
    Observation {
        sequence,
        observed_at: monotonic_milliseconds(),
        session: session.into(),
        epoch: 1,
        admission_epoch: 1,
        eligible: true,
        history_complete: true,
        prompts: Arc::new(vec![]),
    }
}

#[test]
fn asynchronous_round_trip_and_revocation() {
    let client =
        AsyncClient::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture")).to_owned())
            .unwrap();
    assert!(client.publish(observation("valid", 1)));
    let deadline = Instant::now() + Duration::from_secs(3);
    loop {
        if let Some(result) = client.try_result() {
            assert_eq!(result.unwrap().sequence, 1);
            break;
        }
        assert!(Instant::now() < deadline);
        std::thread::sleep(Duration::from_millis(5));
    }
    assert_eq!(client.admission.load(), 3);
    client.invalidate(2);
    assert_eq!(client.admission.load(), 4);
    client.disable();
    assert!(!client.publish(observation("valid", 2)));
    assert_eq!(client.admission.load(), 0);
    assert!(
        client
            .try_result()
            .unwrap()
            .unwrap_err()
            .contains("restart Codex")
    );
}

#[test]
#[ignore = "requires the built .NET helper; set CLICI_TEST_HELPER and CLICI_RENDERER_KEYS=0"]
fn rust_and_dotnet_share_the_admission_abi_and_clock() {
    let path = std::env::var_os("CLICI_TEST_HELPER").expect("set CLICI_TEST_HELPER");
    let client = AsyncClient::start(path.into()).unwrap();
    let start = Instant::now();
    let mut sequence = 0;
    loop {
        sequence += 1;
        client.publish(observation("abi-test", sequence));
        if let Some(result) = client.try_result() {
            let reply = result.expect(".NET v2 handshake and reply");
            assert_eq!(reply.action, "Observed", "{}", reply.reason);
            break;
        }
        assert!(start.elapsed() < Duration::from_secs(5), "no .NET reply");
        std::thread::sleep(Duration::from_millis(50));
    }
}

fn named_fixture(name: &str) -> std::path::PathBuf {
    let original = Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"));
    let directory = original.parent().unwrap().join("latency-fixtures");
    std::fs::create_dir_all(&directory).unwrap();
    let target = directory.join(format!(
        "{name}-{}-{}.exe",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    std::fs::copy(original, &target).unwrap();
    target
}

#[test]
fn startup_hang_never_blocks_native_publication_or_drop() {
    let begin = Instant::now();
    let client = AsyncClient::start(named_fixture("never-ready")).unwrap();
    client.publish(observation("valid", 1));
    assert!(begin.elapsed() < Duration::from_millis(100));
    std::thread::sleep(Duration::from_millis(100));
    let begin = Instant::now();
    client.invalidate(2);
    drop(client);
    assert!(begin.elapsed() < Duration::from_millis(10));
}

#[test]
fn blocked_pipe_write_times_out_without_blocking_publication() {
    let client = AsyncClient::start(named_fixture("never-read")).unwrap();
    let mut frame = observation("valid", 1);
    frame.prompts = Arc::new(
        (0..4096)
            .map(|i| Binding {
                message_id: format!("message-{i}"),
                nonce: "A".repeat(32),
            })
            .collect(),
    );
    assert!(client.publish(frame.clone()));
    std::thread::sleep(Duration::from_millis(100));
    let begin = Instant::now();
    for sequence in 2..102 {
        frame.sequence = sequence;
        client.publish(frame.clone());
    }
    assert!(begin.elapsed() < Duration::from_millis(100));
    let until = Instant::now() + Duration::from_secs(3);
    loop {
        if let Some(result) = client.try_result() {
            assert!(result.is_err());
            break;
        }
        assert!(
            Instant::now() < until,
            "write watchdog did not terminate the helper"
        );
        std::thread::sleep(Duration::from_millis(10));
    }
}

#[test]
fn malformed_or_disconnected_helper_is_not_reused() {
    for session in ["malformed", "disconnect"] {
        let mut client =
            Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
        assert!(client.navigate(&request(session)).unwrap().is_err());
        assert!(!client.is_available());
    }
}

#[test]
#[cfg(windows)]
fn abrupt_parent_exit_leaves_no_helper_process() {
    use std::ffi::c_void;
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut c_void;
        fn WaitForSingleObject(handle: *mut c_void, milliseconds: u32) -> u32;
        fn CloseHandle(handle: *mut c_void) -> i32;
    }
    let fixture = Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"));
    let report = fixture
        .parent()
        .unwrap()
        .join(format!("orphan-{}.txt", uuid::Uuid::new_v4()));
    let begin = Instant::now();
    assert!(
        std::process::Command::new(fixture)
            .arg("--orphan-parent")
            .arg(&report)
            .status()
            .unwrap()
            .success()
    );
    assert!(begin.elapsed() < Duration::from_secs(3));
    let pid = std::fs::read_to_string(report)
        .unwrap()
        .parse::<u32>()
        .unwrap();
    unsafe {
        let process = OpenProcess(0x00100000, 0, pid);
        if !process.is_null() {
            let result = WaitForSingleObject(process, 2000);
            CloseHandle(process);
            assert_eq!(result, 0, "helper survived parent exit");
        }
    }
}

#[test]
fn replacement_worker_starts_clean_after_a_hang() {
    let fixture = Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"));
    let mut client = Client::start(fixture).unwrap();
    assert!(client.navigate(&request("hang")).unwrap().is_err());
    assert!(!client.is_available());
    let mut replacement = Client::start(fixture).unwrap();
    let reply = replacement.navigate(&request("valid")).unwrap().unwrap();
    assert_eq!(reply.sequence, 7);
}

#[test]
fn oversized_reply_refuses_and_disables_the_worker() {
    let mut client = Client::start(Path::new(env!("CARGO_BIN_EXE_bridge-pipe-fixture"))).unwrap();
    assert!(client.navigate(&request("oversized")).unwrap().is_err());
    assert!(client.navigate(&request("valid")).is_none());
}
