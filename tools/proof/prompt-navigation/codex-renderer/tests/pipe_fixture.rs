//! Test-only private child. Never used by the Codex patch.
use std::io::{self, BufRead, Write};

fn main() {
    if std::env::args().nth(1).as_deref() == Some("--orphan-parent") {
        use clici_renderer_bridge_proof::{AsyncClient, Observation, monotonic_milliseconds};
        let client = AsyncClient::start(std::env::current_exe().unwrap()).unwrap();
        client.publish(Observation {
            sequence: 1,
            observed_at: monotonic_milliseconds(),
            session: "valid".into(),
            epoch: 1,
            admission_epoch: 1,
            eligible: true,
            history_complete: true,
            prompts: std::sync::Arc::new(vec![]),
        });
        let until = std::time::Instant::now() + std::time::Duration::from_secs(3);
        loop {
            if let Some(reply) = client.try_result() {
                let pid = reply.unwrap().helper_process_id.unwrap();
                std::fs::write(std::env::args().nth(2).unwrap(), pid.to_string()).unwrap();
                std::process::exit(0); // Deliberately bypass Drop: Job must close at process exit.
            }
            assert!(std::time::Instant::now() < until);
            std::thread::sleep(std::time::Duration::from_millis(5));
        }
    }
    let executable = std::env::current_exe().unwrap();
    let name = executable.file_stem().unwrap().to_string_lossy();
    if name.contains("never-ready") {
        std::thread::sleep(std::time::Duration::from_secs(30));
        return;
    }
    let version = if std::env::var("CLICI_RENDERER_PROTOCOL").as_deref() == Ok("2") {
        let mut handshake = String::new();
        io::stdin().read_line(&mut handshake).unwrap();
        2
    } else {
        1
    };
    println!("{{\"version\":{version},\"ready\":true}}");
    io::stdout().flush().unwrap();
    if name.contains("never-read") {
        std::thread::sleep(std::time::Duration::from_secs(30));
        return;
    }
    for line in io::stdin().lock().lines() {
        let request: serde_json::Value = serde_json::from_str(&line.unwrap()).unwrap();
        let sequence = request["sequence"].as_u64().unwrap();
        match request["sessionId"].as_str().unwrap() {
            "hang" => std::thread::sleep(std::time::Duration::from_secs(30)),
            "oversized" => println!("{}", "x".repeat(5000)),
            "malformed" => println!("not JSON"),
            "disconnect" => return,
            session => println!(
                "{}",
                serde_json::json!({
                    "sequence": if session == "stale" { sequence - 1 } else { sequence },
                    "handled": false,
                    "action": "Observed"
                    ,"helperProcessId": std::process::id()
                })
            ),
        }
        io::stdout().flush().unwrap();
    }
}
