# Codenotch

Claude Code and Codex usage limits in the GNOME panel, plus a notch pinned to
the right edge of the screen. A Linux answer to the macOS menu-bar usage
trackers, built for GNOME on Wayland.

```
panel:  ◔ 88%                 ← mini dial + lowest percent left across providers

notch:  ╭──────╮
        │ (✱)  │  two rings per provider: outer = session window,
        │ 88%  │  inner = weekly. Both drain clockwise from full.
        │wk 63%│
        │ (◎)  │  hover → a callout slides out with a bar per window,
        │ 41%  │  percent left, and "resets in 2h 14m · 17:10" for each,
        │wk 79%│  including scoped caps like Claude's model-specific weekly.
        ╰──────╯
```

Green under 75% used, amber to 90%, red past it. Click the notch or the panel
item for the full popup.

The extension makes no network calls and never touches your credentials. It
reads JSON over localhost from a [CodexBar](https://github.com/steipete/CodexBar)
`serve` process, which owns all provider auth and caching.

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

You need GNOME Shell 45+ and Claude Code and/or Codex already signed in on this
machine. The script needs `curl`, `tar` and `python3`, which a GNOME system
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

Thresholds live in `tone()`: amber at 75% used, red at 90%. The three colours
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
