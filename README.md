# Codenotch

Claude Code and Codex usage limits in the GNOME panel, plus a notch pinned to
the right edge of the screen. A Linux answer to the macOS menu-bar usage
trackers, built for GNOME on Wayland.

The extension makes no network calls and never touches your credentials. It
reads JSON over localhost from a [CodexBar](https://github.com/steipete/CodexBar)
`serve` process, which owns all provider auth and caching.

```
panel:  ⌁ 88%          ← lowest remaining across enabled providers
notch:  ┌────┐
        │ 88 │ Claude   ← session window, hover to expand
        │ 41 │ Codex
        └────┘
```

## Requirements

- GNOME Shell 45 or newer (ESM extension API)
- The CodexBar CLI (Linux build)
- Claude Code and/or Codex already signed in on this machine

## Install

### 1. The CodexBar CLI

There's no Fedora package, so take the glibc Linux tarball from
[CodexBar releases](https://github.com/steipete/CodexBar/releases) — the file is
named `CodexBarCLI-v<tag>-linux-x86_64.tar.gz`.

The tarball holds a `CodexBarCLI` binary *and* a `CodexBar_CodexBarCore.bundle`
directory of provider plugins that has to stay beside it, so install the whole
payload and symlink the entrypoint rather than copying the binary alone:

```fish
mkdir -p ~/.local/libexec/codexbar ~/.local/bin
tar -xzf ~/Downloads/CodexBarCLI-v*-linux-x86_64.tar.gz -C /tmp
cp -r /tmp/CodexBarCLI /tmp/CodexBar_CodexBarCore.bundle /tmp/VERSION ~/.local/libexec/codexbar/
chmod 755 ~/.local/libexec/codexbar/CodexBarCLI
ln -sf ~/.local/libexec/codexbar/CodexBarCLI ~/.local/bin/codexbar
codexbar --version
```

A `libcurl.so.4: no version information available` line on every invocation is a
harmless symbol-versioning notice from the glibc build, not a failure.

If the dynamic loader complains, use the `linux-musl-x86_64` tarball instead —
it's statically linked.

### 2. Enable your providers

```fish
codexbar config providers
codexbar config enable --provider claude
codexbar --provider claude --format json --pretty
```

On Linux the browser-cookie sources aren't available; Claude resolves through
the OAuth credentials Claude Code already wrote, or a CLI PTY fallback. If that
last command prints usage, everything downstream works.

Check the shape once, because the extension's parser depends on it:

```fish
codexbar --provider all --format json | jq
```

Each entry needs `provider`, `usage.primary.usedPercent` and
`usage.primary.resetsAt`. The JSON reports percent *used*; the extension
displays percent *left*.

### 3. The usage server

```fish
mkdir -p ~/.config/systemd/user
cp systemd/codexbar-serve.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now codexbar-serve
curl -s localhost:8787/health
curl -s localhost:8787/usage | jq
```

`serve` binds loopback only, caches responses for `--refresh-interval` seconds,
reloads provider config per request, and falls back to the last good reading for
up to ten intervals when a refresh fails. The panel therefore shows dated
numbers rather than blanking out, and no polling interval in the extension can
provoke an upstream rate limit.

### 4. The extension

```fish
mkdir -p ~/.local/share/gnome-shell/extensions
cp -r "codenotch@oltiir.github.io" ~/.local/share/gnome-shell/extensions/
```

Note the path: it's `gnome-shell/extensions`, not `gnome-extensions`. The shell
silently ignores anything in the latter.

## Activate

```fish
gnome-extensions enable codenotch@oltiir.github.io
gnome-extensions info codenotch@oltiir.github.io
```

On Wayland the shell can't be reloaded in place, so a newly copied extension
won't be picked up until the session restarts. Either log out and back in, or
test it in a nested shell first:

```fish
dbus-run-session -- gnome-shell --devkit
```

Shell 50 removed `--nested` (it fails with `Unknown option --nested`). Use
`--devkit` for a nested shell; plain `--wayland` is *not* nested — it tries to
take the seat and dies with `Failed to take control of the session: EBUSY`
while your real session holds it. On Fedora `--devkit` also logs
`Failed to launch devkit: /usr/libexec/mutter-devkit (No such file or
directory)`; that helper is unpackaged and the nested shell runs regardless.

To check the extension loaded without opening a window at all:

```fish
dbus-run-session -- sh -c '
  gnome-shell --headless --virtual-monitor 1280x720 --wayland-display wl-test &
  sleep 25
  gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell \
    --method org.gnome.Shell.Extensions.GetExtensionInfo codenotch@oltiir.github.io
'
```

`state: 1` is enabled, `state: 3` is an error — and the `error` field says why.

Watch the logs while it starts:

```fish
journalctl --user -f -o cat /usr/bin/gnome-shell
```

To disable or remove:

```fish
gnome-extensions disable codenotch@oltiir.github.io
rm -rf ~/.local/share/gnome-shell/extensions/codenotch@oltiir.github.io
```

## Configuration

At the top of `extension.js`:

| Constant | Default | Notes |
| --- | --- | --- |
| `ENDPOINT` | `http://127.0.0.1:8787/usage` | Must match the port in the systemd unit |
| `POLL_SECONDS` | `30` | The server, not this, controls upstream load |
| `SHOW_EDGE_NOTCH` | `true` | `false` leaves only the panel indicator |
| `BAR_WIDTH` | `168` | Keep in sync with `.cn-bar-track` in `stylesheet.css` |

Thresholds live in `severity()`: amber at 75% used, red at 90%.

## Troubleshooting

**Panel shows ⚠** — the extension couldn't read the endpoint. Check
`systemctl --user status codexbar-serve` and `curl -s localhost:8787/usage`.

**Won't enable** — compare `gnome-shell --version` against the `shell-version`
list in `metadata.json`.

**Popup says "no data" for a provider** — that provider's fetch failed inside
CodexBar. Reproduce it directly: `codexbar --provider <id> --format json -v`.

**Notch in the way** — set `reactive: false` on the `St.BoxLayout` in
`_buildNotch()` to make it click-through, or `SHOW_EDGE_NOTCH = false` to drop
it. (On Shell 45-49 this was `affectsInputRegion: false` in the `addChrome()`
params; Shell 50 rejects that parameter, so reactivity governs it instead.)

## Known limits

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
