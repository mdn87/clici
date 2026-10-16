//! Test-only private child. Never used by the Codex patch.
use std::io::{self, BufRead, Write};

fn main() {
    println!("{{\"version\":1,\"ready\":true}}");
    io::stdout().flush().unwrap();
    for line in io::stdin().lock().lines() {
        let request: serde_json::Value = serde_json::from_str(&line.unwrap()).unwrap();
        let sequence = request["sequence"].as_u64().unwrap();
        match request["sessionId"].as_str().unwrap() {
            "hang" => std::thread::sleep(std::time::Duration::from_secs(30)),
            "oversized" => println!("{}", "x".repeat(5000)),
            session => println!(
                "{}",
                serde_json::json!({
                    "sequence": if session == "stale" { sequence - 1 } else { sequence },
                    "handled": false,
                    "action": "Observed"
                })
            ),
        }
        io::stdout().flush().unwrap();
    }
}
