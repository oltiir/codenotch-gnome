# Codenotch

Claude Code and Codex usage limits in the GNOME panel, plus a notch pinned to
the right edge of the screen. A Linux answer to the macOS menu-bar usage
trackers, built for GNOME on Wayland.

```
panel:  ◔ 68%                 ← mini dial + highest percent used across providers

notch:  ╭──────╮
        │ (✱)  │  two rings per provider: outer = current session,
        │ 68%  │  inner = weekly. Both fill clockwise as you use them.
        │wk 47%│
        │ (◎)  │  hover → a callout laid out like claude.ai's usage panel:
        │ 21%  │  "Current session · Resets in 59 min", a bar, "68% used";
        │wk 12%│  then "Weekly limits": All models, Fable, any other scoped cap.
        ╰──────╯
```

Everything is percent **used**, the way claude.ai reports it. Green under 70%,
yellow from 70%, red from 90%. Click the notch or the panel item for the popup.

The extension makes no network calls and never touches your credentials. It
reads JSON over localhost from a [CodexBar](https://github.com/steipete/CodexBar)
`serve` process, which owns all provider auth and caching.

Not on GNOME? There's a native applet for COSMIC in [`cosmic/`](cosmic/), a
waybar module for Hyprland in [`waybar/`](waybar/), and a tray app for
**Windows 11** in [`windows/`](windows/). See the sections below.

## Setup

```sh
git clone https://github.com/oltiir/codenotch-gnome.git
cd codenotch-gnome
./install.sh
```

**Then log out and log back in.** That's the whole thing.

Wayland cannot load an extension into a running shell, so the panel won't show
it until the session restarts. After that both pieces start on their own at
every login.

You need GNOME Shell 45+ (on COSMIC, see below) and Claude Code and/or Codex
already signed in on this machine. The script needs `curl`, `tar` and `python3`, which a GNOME system
already has.

<details>
<summary>What <code>install.sh</code> does</summary>

1. Downloads the latest CodexBar CLI for your architecture and **verifies its
   published SHA-256** before installing it to `~/.local/libexec/codexbar`,
   symlinked as `~/.local/bin/codexbar`.
2. Enables the providers you're actually signed in to, and disables the ones you
   aren't so they don't sit in the popup reading "no data".
3. Installs and starts `codexbar-serve` as a user service on `127.0.0.1:8787`.
4. Copies the extension to `~/.local/share/gnome-shell/extensions/` and adds it
   to `enabled-extensions`, preserving whatever you already had enabled.

It only touches `~/.local`, `~/.config` and dconf — no sudo, nothing
system-wide. Re-run it any time; it's idempotent, and it's also the upgrade
path.

</details>

## Did it work?

After logging back in:

```sh
gnome-extensions info codenotch@oltiir.github.io    # want: State: ACTIVE
```

If the panel shows **—** and the notch a grey dial marked *offline*, the
extension can't reach the server — a data problem, not an extension problem:

```sh
systemctl --user status codexbar-serve
curl -s localhost:8787/usage | jq
```

## COSMIC / Pop!_OS 24.04

The GNOME extension can't run on COSMIC: COSMIC is System76's Rust desktop and
has no JavaScript extension system. So there's a native **COSMIC applet** in
[`cosmic/`](cosmic/) instead: same dials, same claude.ai-style details, same
70/90 colours, reading the same local server.

No Rust toolchain needed: every [release](https://github.com/oltiir/codenotch-gnome/releases/latest)
carries `codenotch-cosmic-linux-x86_64.tar.gz`, built on Ubuntu 24.04 so it
runs on Pop!_OS 24.04. Extract it and run the two scripts inside:

```sh
./install.sh            # CLI + server; skips the GNOME extension when there's no GNOME Shell
./install-applet.sh     # the applet, per-user, no sudo
```

Then **Settings → Desktop → Panel → Configure panel applets → Add applet →
Codenotch**. To build from source instead, `cd cosmic && just install` from a
checkout. Dependencies, configuration and troubleshooting are in
[`cosmic/README.md`](cosmic/README.md).

Pop!_OS 22.04 (GNOME Shell 42) is out of reach for both: the extension needs
Shell 45+, and 22.04 has no COSMIC panel.

## Omarchy / Hyprland (waybar)

Waybar has no JavaScript extension system either, so there's a **waybar
module** in [`waybar/`](waybar/): the same numbers and the same 70/90
thresholds, drawn in the idiom of the stock `CPU` / `MEM` / `VOL` modules
rather than as dials, since a waybar tooltip is pango markup and can't hold
Cairo rings.

```
AI ██░░░░░░░░ 23%   MEM ████░░░░░░ 41%   CPU ██░░░░░░░░ 18%
```

The root `install.sh` handles the CLI and the server on any Linux, then:

```sh
cd waybar && ./install-waybar.sh
```

It wires `custom/codenotch` into your existing bar by parsing the config, so
your own modules and comments survive, and it restarts waybar in place -- no
logout. Hovering gives the claude.ai-style breakdown; clicking opens it in a
floating terminal. Configuration, the reason it runs continuously rather than
on a waybar `interval`, and the tests are in
[`waybar/README.md`](waybar/README.md).

## Windows 11

A native **tray app** in [`windows/`](windows/): a tray icon with the mini
dial, a flyout with the full per-provider breakdown, and an edge notch pinned
to the right of the primary monitor with a hover callout — the same dials, the
same claude.ai-style details, the same 70/90 colours as the GNOME one.

**Install.** In any PowerShell window:

```powershell
irm https://raw.githubusercontent.com/oltiir/codenotch-gnome/main/windows/get.ps1 | iex
```

That picks the build for your architecture, checks its published SHA-256 and
installs per-user: no admin, no .NET on the machine, and nothing to do about
the execution policy, since piping into `iex` never needed one. It starts
Codenotch right away and again at every login, as a dial in the notification
area beside the clock. Claude Code and/or Codex have to be signed in on this
machine already — that's where the numbers come from.

Prefer to see what you run? Download the zip from the
[release page](https://github.com/oltiir/codenotch-gnome/releases/latest),
extract it and double-click `install.cmd` — or just run `Codenotch.exe` out of
the extracted folder to try it without installing anything.

The exe is unsigned, so on first run SmartScreen may say **Windows protected
your PC**: choose *More info → Run anyway*. The `.sha256` next to the zip on
the release page is how to check the download before that.

One difference worth knowing: because CodexBar is Swift and has no Windows
build, this port reads the credential files Claude Code and Codex already
wrote and sends them only to those vendors' own endpoints. It stores nothing
and adds no telemetry, but this is not the "never touches credentials" model
the other three ports use; set `endpoint` in its settings to a local `codexbar
serve` (handy from WSL) and it goes back to that model.

See [`windows/README.md`](windows/README.md) for configuration and
troubleshooting.

## Configuration

At the top of `extension.js`:

| Constant | Default | Notes |
| --- | --- | --- |
| `ENDPOINT` | `http://127.0.0.1:8787/usage` | Must match the port in the systemd unit |
| `POLL_SECONDS` | `30` | The server, not this, controls upstream load |
| `SHOW_EDGE_NOTCH` | `true` | `false` leaves only the panel indicator |
| `RING_SIZE` | `46` | Dial diameter in the notch, px |
| `PANEL_RING_SIZE` | `14` | Mini dial in the top bar, px |
| `BAR_WIDTH` | `120` | Bars in the hover callout; the popup uses this + 40 |

Thresholds are `WARN_AT = 70` and `CRITICAL_AT = 90` (percent used). The three colours
are defined once in `TONE` (extension.js, for the Cairo rings) and once in
`stylesheet.css` (for text and bars); change both. Re-run
`./install.sh` after editing, then log out and back in.

## Uninstall

```sh
gnome-extensions disable codenotch@oltiir.github.io
rm -rf ~/.local/share/gnome-shell/extensions/codenotch@oltiir.github.io
systemctl --user disable --now codexbar-serve
rm -f ~/.config/systemd/user/codexbar-serve.service
rm -rf ~/.local/libexec/codexbar ~/.local/bin/codexbar
```

## Troubleshooting

**Popup says "no data" for a provider** — that provider's fetch failed inside
CodexBar. Reproduce it directly:

```sh
codexbar --provider claude --format json -v
```

**Won't enable** — compare `gnome-shell --version` against the `shell-version`
list in `metadata.json`.

**Notch in the way** — set `reactive: false` on the `St.BoxLayout` in
`_buildNotch()` to make it click-through, or `SHOW_EDGE_NOTCH = false` to drop
it. On Shell 45–49 this was `affectsInputRegion: false` in the `addChrome()`
params; **Shell 50 rejects that parameter outright** and the extension fails to
load with `Unrecognized parameter`, so reactivity governs it there instead.

**`libcurl.so.4: no version information available`** on every `codexbar` call —
harmless symbol-versioning notice from the glibc build. Use the
`linux-musl-x86_64` tarball if the loader genuinely fails.

**Testing without logging out** — you can load the extension in a nested shell,
though the notch and panel appear only inside that nested session:

```sh
dbus-run-session -- gnome-shell --devkit
```

Shell 50 removed `--nested`; plain `--wayland` is *not* nested — it tries to
take the seat and dies with `EBUSY` while your real session holds it. On Fedora
`--devkit` also logs a missing `/usr/libexec/mutter-devkit`, which is
unpackaged, and draws no window as a result. To check load state with no window
at all:

```sh
dbus-run-session -- sh -c '
  gnome-shell --headless --virtual-monitor 1280x720 --wayland-display wl-test &
  sleep 25
  gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell \
    --method org.gnome.Shell.Extensions.GetExtensionInfo codenotch@oltiir.github.io
'
```

`state: 1` is enabled, `state: 3` is an error, and the `error` field says why.

## Notes

- The server binds loopback only, caches for `--refresh-interval` seconds, and
  falls back to the last good reading for up to ten intervals when a refresh
  fails — so the panel shows dated numbers rather than blanking, and no polling
  interval in the extension can provoke an upstream rate limit.
- Extensions must live in `~/.local/share/gnome-shell/extensions/`, **not**
  `~/.local/share/gnome-extensions/`. The shell ignores the latter silently,
  with no error anywhere.
- The CodexBar tarball ships a `CodexBar_CodexBarCore.bundle` of provider
  plugins that has to stay beside the binary — installing the binary alone
  orphans it.
- The notch sits on the primary monitor, vertically centred, and re-places on
  `monitors-changed`.
- No preferences UI; changing behaviour means editing `extension.js`. A real one
  needs a GSettings schema plus `prefs.js`.
- Quota only. "Is Claude still working right now?" isn't available from
  CodexBar — that would need a separate watcher over `~/.claude/projects`.

## Credits

Data layer: [CodexBar](https://github.com/steipete/CodexBar) by Peter
Steinberger (MIT). Concept borrowed from
[codenotch](https://github.com/vinzdg/codenotch) for macOS.

MIT licensed.
