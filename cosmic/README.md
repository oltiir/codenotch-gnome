# Codenotch for COSMIC

Claude Code and Codex usage limits in the COSMIC panel. This is the COSMIC
counterpart of the GNOME extension one directory up: the same concentric dials,
the same claude.ai-style details, the same colours, reading the same local
`codexbar serve` process. Built for COSMIC Epoch on Pop!_OS 24.04 and any other
distro that ships COSMIC.

```
panel:   ◔ 68%             ← mini dial + highest percent used across providers

popup:   (✱)  Claude
              Resets in 59 min  ·  17:10

         Current session    ████████░░░░   68% used
         Resets in 59 min · 17:10

         Weekly limits
         All models         █████░░░░░░░   47% used
         Resets in 13 hr 49 min · Tue 06:00
         Fable              ███████░░░░░   64% used
         Resets in 13 hr 49 min · Tue 06:00
```

Everything is percent **used**, the way claude.ai reports it. Green under 70%,
yellow from 70%, red from 90%. Hovering the panel item opens the popup
(`X-CosmicHoverPopup=Auto` in the desktop entry); clicking toggles it.

The applet makes no network calls beyond loopback and never touches provider
credentials. [CodexBar](https://github.com/steipete/CodexBar) owns all of that.

## Install the prebuilt binary

Every release carries `codenotch-cosmic-linux-x86_64.tar.gz`, built by CI on
Ubuntu 24.04 -- the base of Pop!_OS 24.04 -- so it links against a glibc that
Pop!_OS actually has. No Rust toolchain, no sudo. Claude Code must already be
logged in on the machine.

```sh
cd ~/Downloads
curl -fsSLO https://github.com/oltiir/codenotch-gnome/releases/latest/download/codenotch-cosmic-linux-x86_64.tar.gz
curl -fsSLO https://github.com/oltiir/codenotch-gnome/releases/latest/download/codenotch-cosmic-linux-x86_64.tar.gz.sha256
sha256sum -c codenotch-cosmic-linux-x86_64.tar.gz.sha256
tar -xzf codenotch-cosmic-linux-x86_64.tar.gz
cd codenotch-cosmic-*/
./install.sh            # CodexBar CLI + codexbar-serve user service (skips the GNOME extension)
./install-applet.sh     # binary, desktop entry and icon into ~/.local
```

Then **Settings → Desktop → Panel → Configure panel applets → Add applet →
Codenotch**.

## Build from source

**1. The data side.** From the repo root, the same installer as GNOME. On a
machine without GNOME Shell it installs the CodexBar CLI and the
`codexbar-serve` user service, then stops:

```sh
git clone https://github.com/oltiir/codenotch-gnome.git
cd codenotch-gnome
./install.sh
```

**2. Build dependencies.** A Rust toolchain plus the C libraries libcosmic
links against.

```sh
# Pop!_OS / Ubuntu / Debian
sudo apt install cargo rustc just libxkbcommon-dev libwayland-dev \
    libfontconfig-dev libfreetype-dev libexpat1-dev pkg-config

# Fedora
sudo dnf install cargo rust just libxkbcommon-devel wayland-devel \
    fontconfig-devel freetype-devel expat-devel
```

Rust 1.85 or newer (the crate is edition 2024). Ubuntu 24.04's packaged Rust is
1.75, so use [rustup](https://rustup.rs) there.

**3. Build and install.** Per-user, no sudo:

```sh
cd cosmic
just install
```

This builds `--release`, installs the binary to `~/.local/bin`, the applet
desktop entry to `~/.local/share/applications` with an absolute `Exec=` so the
panel finds it regardless of `PATH`, and the icon. `install-applet.sh` does the
same from an already-built binary.

**4. Add it to the panel.** Settings → Desktop → Panel → Configure panel
applets → Add applet → **Codenotch**.

## Did it work?

The panel item shows a small dial and a percentage within a few seconds. If it
shows **—** and a grey dial, the applet can't reach the server, which is a data
problem rather than an applet problem:

```sh
systemctl --user status codexbar-serve
curl -s localhost:8787/usage | jq
```

To run it outside the panel for a quick look (needs a COSMIC session):

```sh
just run
```

## Configuration

| Where | Constant | Default | Notes |
| --- | --- | --- | --- |
| `src/usage.rs` | `HOST`, `PORT`, `PATH` | `127.0.0.1`, `8787`, `/usage` | Must match the systemd unit |
| `src/usage.rs` | `POLL` | 30 s | The server, not this, controls upstream load |
| `src/usage.rs` | `WARN_AT`, `CRITICAL_AT` | 70, 90 | Percent used at which colour turns |
| `src/usage.rs` | `Tone::rgb()` | | The three colours, same as the GNOME stylesheet |
| `src/app.rs` | `POPUP_RING`, `BAR_WIDTH`, `LABEL_WIDTH` | 56, 150, 160 px | Popup layout |

Re-run `just install` after editing. The panel restarts applets when their
desktop entry changes; if it doesn't pick the new binary up, remove and re-add
the applet in Settings.

## Uninstall

```sh
just uninstall     # from a checkout; or remove the three files install-applet.sh listed
```

Remove it from the panel in Settings first, or the panel will log a failed
launch until you do.

## Development

```sh
just check      # clippy, pedantic
just test       # unit tests for the parser and reset-time formatting
```

`src/usage.rs` has no UI dependencies and mirrors `windowsFrom()`, `tone()` and
the countdown text in the GNOME extension, so the two front-ends stay in
agreement about what a window is called and when it turns yellow.

## Layout

```
src/main.rs         entry point: cosmic::applet::run
src/app.rs          the applet: panel item, popup, fetch scheduling
src/draw.rs         canvas programs for the dials and bars
src/usage.rs        HTTP fetch, JSON parsing, window model, thresholds
resources/          applet desktop entry and icon
justfile            build / install / uninstall from source
install-applet.sh   install a prebuilt binary (what the release tarball runs)
```

MIT licensed, like the rest of the repo.
