// SPDX-License-Identifier: MIT
//
// Data layer: fetch `codexbar serve`'s JSON over a plain HTTP GET on loopback
// and turn it into the windows the UI draws. Mirrors windowsFrom()/tone()/
// countdown() in the GNOME extension so both front-ends agree.

use chrono::{DateTime, Local};
use serde::Deserialize;
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;

// ---- knobs -----------------------------------------------------------------

pub const HOST: &str = "127.0.0.1";
pub const PORT: u16 = 8787;
pub const PATH: &str = "/usage";
pub const POLL: Duration = Duration::from_secs(30);

/// Percent *used* at which the colour turns. Mirrors claude.ai's usage panel.
pub const WARN_AT: u8 = 70;
pub const CRITICAL_AT: u8 = 90;

// ---- model -----------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Tone {
    Ok,
    Warn,
    Critical,
    Stale,
}

impl Tone {
    pub fn of(used: u8) -> Self {
        if used >= CRITICAL_AT {
            Tone::Critical
        } else if used >= WARN_AT {
            Tone::Warn
        } else {
            Tone::Ok
        }
    }

    /// Same three colours as the GNOME stylesheet: #57e389 / #f8e45c / #ff7b63.
    pub fn rgb(self) -> (f32, f32, f32) {
        match self {
            Tone::Ok => (0.34, 0.89, 0.54),
            Tone::Warn => (0.97, 0.89, 0.36),
            Tone::Critical => (1.00, 0.48, 0.39),
            Tone::Stale => (0.60, 0.60, 0.59),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WindowKey {
    Session,
    Weekly,
    Other,
}

#[derive(Debug, Clone)]
pub struct Window {
    pub key: WindowKey,
    /// Named the way claude.ai names them: "Current session", "All models", "Fable".
    pub label: String,
    pub used: u8,
    pub resets_at: Option<DateTime<Local>>,
    /// True for everything under claude.ai's "Weekly limits" heading.
    pub weekly_group: bool,
}

impl Window {
    /// "Resets in 13 hr 49 min  ·  Tue 06:00", or "" when unknown.
    pub fn reset_line(&self) -> String {
        let Some(at) = self.resets_at else { return String::new() };
        let now = Local::now();
        if at <= now {
            return "Resetting".into();
        }
        let mins = (at - now).num_minutes().max(1);
        let countdown = if mins < 60 {
            format!("{mins} min")
        } else if mins < 24 * 60 {
            let (h, m) = (mins / 60, mins % 60);
            if m == 0 { format!("{h} hr") } else { format!("{h} hr {m} min") }
        } else {
            let (d, h) = (mins / (24 * 60), (mins % (24 * 60)) / 60);
            let day = if d == 1 { "day" } else { "days" };
            if h == 0 { format!("{d} {day}") } else { format!("{d} {day} {h} hr") }
        };
        let clock = if at.date_naive() == now.date_naive() {
            at.format("%H:%M").to_string()
        } else {
            at.format("%a %H:%M").to_string()
        };
        format!("Resets in {countdown}  \u{b7}  {clock}")
    }
}

#[derive(Debug, Clone)]
pub struct Provider {
    /// CodexBar's provider id ("claude", "codex"). Not shown in the UI; kept
    /// for logging and tests.
    #[allow(dead_code)]
    pub id: String,
    pub name: String,
    pub glyph: &'static str,
    pub windows: Vec<Window>,
    /// CodexBar's error message when the provider produced no usage.
    pub error: Option<String>,
}

impl Provider {
    pub fn session(&self) -> Option<&Window> {
        self.windows
            .iter()
            .find(|w| w.key == WindowKey::Session)
            .or(self.windows.first())
    }

    pub fn weekly(&self) -> Option<&Window> {
        self.windows.iter().find(|w| w.key == WindowKey::Weekly)
    }
}

fn identity(id: &str) -> (String, &'static str) {
    match id {
        "claude" => ("Claude".into(), "\u{2731}"),  // ✱
        "codex" => ("Codex".into(), "\u{25CE}"),    // ◎
        "cursor" => ("Cursor".into(), "\u{25B3}"),  // △
        "copilot" => ("Copilot".into(), "\u{2318}"), // ⌘
        "gemini" => ("Gemini".into(), "\u{2726}"),  // ✦
        other => {
            let mut c = other.chars();
            let name = match c.next() {
                Some(f) => f.to_uppercase().collect::<String>() + c.as_str(),
                None => "Unknown".into(),
            };
            (name, "\u{25CF}") // ●
        }
    }
}

// ---- wire format -----------------------------------------------------------

#[derive(Deserialize)]
struct RawEntry {
    provider: Option<String>,
    usage: Option<RawUsage>,
    error: Option<RawError>,
}

#[derive(Deserialize)]
struct RawError {
    message: Option<String>,
}

#[derive(Deserialize, Default)]
struct RawUsage {
    primary: Option<RawWindow>,
    secondary: Option<RawWindow>,
    tertiary: Option<RawWindow>,
    #[serde(default, rename = "extraRateWindows")]
    extra_rate_windows: Vec<RawExtra>,
}

#[derive(Deserialize)]
struct RawWindow {
    #[serde(rename = "usedPercent")]
    used_percent: Option<f64>,
    #[serde(rename = "resetsAt")]
    resets_at: Option<String>,
}

#[derive(Deserialize)]
struct RawExtra {
    title: Option<String>,
    window: Option<RawWindow>,
}

fn window(key: WindowKey, label: &str, weekly_group: bool, raw: &Option<RawWindow>) -> Option<Window> {
    let raw = raw.as_ref()?;
    let used = raw.used_percent?.round().clamp(0.0, 100.0) as u8;
    let resets_at = raw
        .resets_at
        .as_deref()
        .and_then(|s| DateTime::parse_from_rfc3339(s).ok())
        .map(|t| t.with_timezone(&Local));
    Some(Window { key, label: label.to_string(), used, resets_at, weekly_group })
}

/// `codexbar serve` mirrors the CLI's JSON: an array for several providers,
/// a single object for one. Be liberal about the envelope.
pub fn parse(text: &str) -> Result<Vec<Provider>, String> {
    let value: serde_json::Value = serde_json::from_str(text).map_err(|e| e.to_string())?;
    let entries: Vec<serde_json::Value> = match value {
        serde_json::Value::Array(a) => a,
        serde_json::Value::Object(ref o) => ["providers", "usages", "results", "items"]
            .iter()
            .find_map(|k| o.get(*k).and_then(|v| v.as_array()).cloned())
            .unwrap_or_else(|| vec![value.clone()]),
        _ => Vec::new(),
    };

    let mut out = Vec::new();
    for entry in entries {
        let Ok(raw) = serde_json::from_value::<RawEntry>(entry) else { continue };
        let id = raw.provider.unwrap_or_else(|| "unknown".into());
        let (name, glyph) = identity(&id);
        let usage = raw.usage.unwrap_or_default();

        let mut windows = Vec::new();
        windows.extend(window(WindowKey::Session, "Current session", false, &usage.primary));
        windows.extend(window(WindowKey::Weekly, "All models", true, &usage.secondary));
        windows.extend(window(WindowKey::Other, "Model", true, &usage.tertiary));
        for extra in &usage.extra_rate_windows {
            // CodexBar titles these "Fable only"; claude.ai just says "Fable".
            let title = extra.title.as_deref().unwrap_or("Scoped");
            let label = title.trim_end_matches(" only").trim_end_matches(" Only");
            windows.extend(window(WindowKey::Other, label, true, &extra.window));
        }

        let error = if windows.is_empty() {
            Some(
                raw.error
                    .and_then(|e| e.message)
                    .unwrap_or_else(|| "no data".into()),
            )
        } else {
            None
        };

        out.push(Provider { id, name, glyph, windows, error });
    }
    Ok(out)
}

// ---- transport -------------------------------------------------------------

/// One HTTP/1.1 GET, no TLS, read to EOF. The server answers with a
/// Content-Length and `Connection: close`; a cold cache can take a while, so
/// the timeout is generous.
pub async fn fetch() -> Result<Vec<Provider>, String> {
    let addr = format!("{HOST}:{PORT}");
    let io = async {
        let mut s = TcpStream::connect(&addr).await.map_err(|e| e.to_string())?;
        let req = format!(
            "GET {PATH} HTTP/1.1\r\nHost: {addr}\r\nConnection: close\r\nAccept: application/json\r\n\r\n"
        );
        s.write_all(req.as_bytes()).await.map_err(|e| e.to_string())?;
        let mut buf = Vec::with_capacity(4096);
        s.read_to_end(&mut buf).await.map_err(|e| e.to_string())?;
        Ok::<Vec<u8>, String>(buf)
    };
    let buf = tokio::time::timeout(Duration::from_secs(45), io)
        .await
        .map_err(|_| "timed out".to_string())??;

    let text = String::from_utf8_lossy(&buf);
    let (head, body) = text.split_once("\r\n\r\n").ok_or("malformed HTTP response")?;
    let status = head
        .lines()
        .next()
        .and_then(|l| l.split_whitespace().nth(1))
        .unwrap_or("0");
    if status != "200" {
        return Err(format!("HTTP {status}"));
    }
    let body = if head.to_ascii_lowercase().contains("transfer-encoding: chunked") {
        dechunk(body)
    } else {
        body.to_string()
    };
    parse(&body)
}

fn dechunk(body: &str) -> String {
    let mut out = String::new();
    let mut rest = body;
    loop {
        let Some((size_line, after)) = rest.split_once("\r\n") else { break };
        let Ok(size) = usize::from_str_radix(size_line.trim().split(';').next().unwrap_or(""), 16)
        else { break };
        if size == 0 {
            break;
        }
        let chunk = after.get(..size).unwrap_or(after);
        out.push_str(chunk);
        rest = after.get(size + 2..).unwrap_or("");
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_codexbar_shape() {
        let json = r#"[{"provider":"claude","source":"claude","usage":{
            "primary":{"usedPercent":46,"resetsAt":"2026-09-07T15:10:00Z","windowMinutes":300},
            "secondary":{"usedPercent":44,"resetsAt":"2026-09-08T04:00:00Z","windowMinutes":10080},
            "tertiary":null,
            "extraRateWindows":[{"id":"claude-weekly-scoped-fable","title":"Fable only",
              "window":{"usedPercent":59,"resetsAt":"2026-09-08T04:00:00Z"}}]}}]"#;
        let p = parse(json).unwrap();
        assert_eq!(p.len(), 1);
        let c = &p[0];
        assert_eq!(c.name, "Claude");
        assert_eq!(c.windows.len(), 3);
        assert_eq!(c.session().unwrap().used, 46);
        assert_eq!(c.weekly().unwrap().used, 44);
        assert_eq!(c.windows[2].label, "Fable");
        assert!(c.windows[2].weekly_group);
        assert!(c.error.is_none());
    }

    #[test]
    fn error_entry_has_no_windows() {
        let json = r#"[{"provider":"codex","error":{"message":"No available fetch strategy for codex."}}]"#;
        let p = parse(json).unwrap();
        assert_eq!(p[0].error.as_deref(), Some("No available fetch strategy for codex."));
        assert!(p[0].windows.is_empty());
    }

    #[test]
    fn thresholds_match_claude() {
        assert_eq!(Tone::of(69), Tone::Ok);
        assert_eq!(Tone::of(70), Tone::Warn);
        assert_eq!(Tone::of(89), Tone::Warn);
        assert_eq!(Tone::of(90), Tone::Critical);
    }

    #[test]
    fn dechunks() {
        assert_eq!(dechunk("5\r\nhello\r\n1\r\n!\r\n0\r\n\r\n"), "hello!");
    }
}
