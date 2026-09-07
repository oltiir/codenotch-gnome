# Codenotch for waybar

Claude Code and Codex usage limits in the waybar, in the same idiom as the
stock `CPU` / `MEM` / `VOL` modules. Built for Omarchy on Hyprland, but nothing
here is Omarchy-specific beyond one restart command.

```
bar:      AI ██░░░░░░░░ 23%   MEM ████░░░░░░ 41%   CPU ██░░░░░░░░ 18%

tooltip:  ✱ Claude
            Current session  ██░░░░░░░░  23%
              Resets in 2 hr 56 min  ·  22:10
            Weekly limits
            All models       █████░░░░░  52%
              Resets in 10 hr 45 min  ·  Tue 05:59
            Fable            ███████░░░  73%
              Resets in 10 hr 45 min  ·  Tue 05:59
            23% in reserve | Expected 41% used | Lasts until reset
          ◎ Codex
            no data · Codex returned invalid data: codex app-server closed stdout
```

Everything is percent **used**, the way claude.ai reports it. The bar carries
the highest *session* percent across providers -- the same figure the GNOME
panel indicator shows -- and the tooltip carries every window for every
provider. Pine under 70%, gold from 70%, love from 90%.

## Setup

The module reads JSON from the same `codexbar serve` process as the GNOME
extension, so the root `install.sh` comes first:

```sh
./install.sh              # CodexBar CLI + the localhost usage server
cd waybar
./install-waybar.sh       # the module, wired into your existing bar
```

No logout needed, unlike the GNOME extension -- waybar restarts in place.

`install-waybar.sh` is idempotent, and it's also the upgrade path. It only
touches `~/.local` and `~/.config`, never sudo. It:

1. Installs `codenotch-waybar` to `~/.local/bin/`.
2. Adds `custom/codenotch` to the front of `modules-right` and appends the
   styles, **by parsing your config** rather than appending text blindly, so
   your own modules, key order and comments come through untouched. Both files
   are backed up with a timestamp first.
3. Restarts waybar (`omarchy restart waybar`, or `SIGUSR2` off Omarchy).
4. Reads the quota once and prints what the bar is about to show.

Everything it writes sits between `>>> codenotch` / `<<< codenotch` markers,
which is what lets a re-run replace its own work instead of stacking another
copy. `./install-waybar.sh --uninstall` removes both and restores the files.

If your waybar config lives somewhere else, point at it:
`WAYBAR_CONFIG_DIR=~/.config/waybar-laptop ./install-waybar.sh`.

## Did it work?

Click the module: it opens a floating terminal with the same breakdown as the
tooltip. Or ask directly:

```sh
codenotch-waybar --print   # the reading as plain text, from the last fetch
codenotch-waybar --once    # the raw waybar JSON, from a fresh fetch
```

A grey `AI ░░░░░░░░░░ —` means the module can't reach the server -- a data
problem, not a module problem:

```sh
systemctl --user status codexbar-serve
curl -s localhost:8787/usage | jq
```

## Why it runs continuously instead of on an interval

`codexbar serve` goes upstream on **every** `/usage` request in 0.56.8 -- its
`--refresh-interval` governs the dashboard snapshot, not this endpoint -- so a
call costs as long as the slowest enabled provider takes to answer. On one
machine with Claude and Codex both enabled that is ~10 s (Claude 7.3 s, Codex
2.8 s). Measure yours:

```sh
time curl -s localhost:8787/usage >/dev/null
```

A waybar `interval` of 30 s against a 10 s call would leave the script running
a third of the time, hit upstream twice a minute, and show an empty module for
the first 10 s after every waybar start. So the module is a continuous `exec`
instead: it prints its last reading immediately -- kept in
`$XDG_RUNTIME_DIR/codenotch-waybar.json`, so a fresh bar is populated at once
-- then fetches every `REFRESH` seconds and prints each new one.

`restart-interval: 30` in the module definition is the safety net: if the
script ever dies, waybar starts it again within 30 s. That is also how to force
a refresh by hand, at the cost of waiting out that interval:

```sh
pkill -f codenotch-waybar
```

## Configuration

At the top of `codenotch-waybar`:

| Constant | Default | Notes |
| --- | --- | --- |
| `ENDPOINT` | `http://127.0.0.1:8787/usage` | Must match the port in the systemd unit |
| `REFRESH` | `120` | Seconds between fetches. Each one goes upstream |
| `TIMEOUT` | `45` | Must exceed the sum of the enabled providers' own timeouts |
| `LABEL` | `AI` | The text before the gauge |
| `CELLS` | `10` | Gauge width, matching the stock CPU/MEM modules |

Thresholds are `WARN_AT = 70` and `CRITICAL_AT = 90` (percent used). The
colours live in `COLOURS` (for the gauge, which is inline pango markup because
a percentage can't be expressed in CSS) and again in `style.css` (for the label
and the figure); change both. Then re-run `./install-waybar.sh`.

The gauge uses Rosé Pine **Moon**'s pine `#3e8fb0` rather than the base
variant's `#31748f`, which sits at 3.4:1 on the bar background -- as dim as the
grey reserved for de-emphasis, and noticeably darker than every neighbouring
module. Gold and love are the base variant's.

## Tests

Pure functions, stdlib only, no pytest:

```sh
cd tests && python3 -m unittest discover
```

The fixtures are real `codexbar serve` output, including a provider that
failed, since "no data" is a state the bar has to render rather than crash on.

## Notes

- The tooltip is pango markup, so the GNOME extension's Cairo dials don't port;
  the block gauge is the waybar-native equivalent and matches the stock modules
  cell for cell. Rows are wrapped in `<tt>` so the gauges line up whatever font
  the bar uses.
- Provider-supplied text is escaped into the markup, not interpreted as it --
  CodexBar titles windows things like `Fable only`, and a `&` in one would
  otherwise break the tooltip.
- Providers are listed in the order of the `PROVIDERS` map rather than the
  order CodexBar answers in, so the tooltip doesn't reshuffle between fetches.
- A provider that errors keeps its block and says `no data` with the reason,
  instead of vanishing and leaving you wondering.
- Only a spent quota fills all ten cells: rounding alone would draw 95% and
  100% identically, reading as exhausted while a twentieth is still there.
- The module goes stale-grey rather than showing `0%` when nothing is enabled
  or every provider failed -- zero would read as "nothing used yet".
