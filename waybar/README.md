# Codenotch for waybar

Claude Code and Codex usage limits in the waybar, one module per provider, in
the same idiom as the stock `CPU` / `MEM` / `VOL` modules. Built for Omarchy on
Hyprland, but nothing here is Omarchy-specific beyond one restart command.

```
bar:      … BAT FULL   CC ████░░░░░░ 41%   CDX ░░░░░░░░░░ 03%
                       ^^^^^^^^^^^^^^^^^ coral   ^^^^^^^^^^^^^^^^ pine

hover CC:                          hover CDX:
  ◉ Claude                           ◎ Codex
    Current session  ████░░░░░░  39%   Current session  ░░░░░░░░░░   3%
      Resets in 1 hr 54 min · 22:10      Resets in 4 hr 31 min · Tue 00:47
    Weekly limits · Tue 06:00          Weekly limits · Mon 19:47
    All models       █████░░░░░  54%   All models       ░░░░░░░░░░   1%
    Fable            ████████░░  75%   6% in reserve | Expected 9% used
    22% in reserve | Expected 61% used
```

Everything is percent **used**, the way claude.ai reports it. Each module shows
its own provider's *session* window and carries that provider's whole
breakdown in its own tooltip — hover Claude, get only Claude.

A module wears its provider's own colour — Anthropic's coral for Claude —
while the quota is healthy, which is nearly all the time. Past 70% the gauge
and figure turn gold, past 90% red; the label keeps the provider's colour
throughout, so a warning module is still identifiable at a glance.

## Setup

The modules read JSON from the same `codexbar serve` process as the GNOME
extension, so the root `install.sh` comes first:

```sh
./install.sh              # CodexBar CLI + the localhost usage server
cd waybar
./install-waybar.sh       # the modules, wired into your existing bar
```

No logout needed, unlike the GNOME extension — waybar restarts in place.

`install-waybar.sh` is idempotent, and it's also the upgrade path. It only
touches `~/.local` and `~/.config`, never sudo. It:

1. Installs `codenotch-waybar` to `~/.local/bin/`.
2. Asks the server which providers are actually enabled, and installs one
   module per provider — in a fixed order, so the bar doesn't shuffle between
   installs just because the server answered in a different order.
3. Appends them to the **end of `modules-left`** and appends the styles, **by
   parsing your config** rather than appending text blindly, so your own
   modules, key order and comments come through untouched. Both files are
   backed up with a timestamp first.
4. Restarts waybar (`omarchy restart waybar`, or `SIGUSR2` off Omarchy).
5. Reads each quota once and prints what the bar is about to show.

Everything it writes sits between `>>> codenotch` / `<<< codenotch` markers,
which is what lets a re-run replace its own work instead of stacking another
copy. `./install-waybar.sh --uninstall` removes them and restores the files.

If your waybar config lives somewhere else, point at it:
`WAYBAR_CONFIG_DIR=~/.config/waybar-laptop ./install-waybar.sh`.

### Why the end of modules-left

Because that is usually where a full bar has room. Measured on the bar this was
built for: the right-hand group ran to within **15px** of the workspace
indicators — about two characters — while **333px** sat unused between the
left-hand group and the centre. A second module costs 16px of padding before it
draws anything, so there was no version of this that fitted on the right.

Measure your own before assuming: screenshot the bar and look, or move the two
`custom/codenotch-*` entries to `modules-right` by hand if that is where your
space is. Nothing else depends on which group they sit in.

## Did it work?

Click a module: it opens a floating terminal with the same breakdown as its
tooltip. Or ask directly:

```sh
codenotch-waybar --print --provider claude   # the reading as plain text
codenotch-waybar --once  --provider claude   # the raw waybar JSON
codenotch-waybar --print                     # every provider at once
```

A grey `CC ░░░░░░░░░░ —` means the module can't reach the server — a data
problem, not a module problem:

```sh
systemctl --user status codexbar-serve
curl -s localhost:8787/usage | jq
```

A module reading `not enabled in CodexBar` means the server never mentioned
that provider: sign in, then re-run `./install-waybar.sh`.

## Why it runs continuously instead of on an interval

`codexbar serve` goes upstream on **every** `/usage` request in 0.56.8 — its
`--refresh-interval` governs the dashboard snapshot, not this endpoint — so a
call costs as long as the slowest enabled provider takes to answer. On one
machine with Claude and Codex both enabled that is ~10 s (Claude 7.3 s, Codex
2.8 s). Measure yours:

```sh
time curl -s localhost:8787/usage >/dev/null
```

A waybar `interval` of 30 s against a 10 s call would leave the script running
a third of the time, hit upstream twice a minute, and show an empty module for
the first 10 s after every waybar start. So each module is a continuous `exec`
that paces itself: it redraws every `POLL` seconds so the countdowns stay
honest, and goes upstream only when the shared reading is older than `REFRESH`.

**The reading is shared.** Both modules read and write one file,
`$XDG_RUNTIME_DIR/codenotch-waybar.json`, holding the raw server response and
when it was taken. Whichever module wakes to a stale cache pays for the fetch;
the other reads its answer. So two modules cost the server no more than one —
and a freshly started bar paints from that file immediately instead of sitting
empty through a fetch. The file holds the *raw* reading rather than a rendered
payload, because the other provider's module has to render its own half of it,
and a rendered payload has its countdowns frozen at the moment it was built.

`restart-interval: 30` in each module definition is the safety net: if a script
ever dies, waybar starts it again within 30 s. That is also how to force a
refresh by hand, at the cost of waiting out that interval:

```sh
pkill -f codenotch-waybar
```

## Configuration

At the top of `codenotch-waybar`:

| Constant | Default | Notes |
| --- | --- | --- |
| `ENDPOINT` | `http://127.0.0.1:8787/usage` | Must match the port in the systemd unit |
| `REFRESH` | `120` | Seconds before the shared reading is refetched |
| `POLL` | `30` | Seconds between redraws, so countdowns stay honest |
| `TIMEOUT` | `45` | Must exceed the sum of the enabled providers' own timeouts |
| `LABELS` | `CC`, `CDX`, … | Per-provider bar labels |
| `PROVIDER_COLOURS` | coral for Claude | A provider's own hue; the rest use `COLOURS["ok"]` |
| `CELLS` | `10` | Gauge width, matching the stock CPU/MEM modules |

Thresholds are `WARN_AT = 70` and `CRITICAL_AT = 90` (percent used). Every
colour lives in `COLOURS` and `PROVIDER_COLOURS`, and nowhere else: a percentage
can't be expressed in CSS, so the colours are inline pango markup and the
stylesheet keeps only the spacing. Re-run `./install-waybar.sh` after editing.

### What a provider colour costs, and what it doesn't

A module says two things at once: which provider it is, and how close to the
limit it is. Anthropic's coral `#d97757` sits **24 degrees of hue** from the
70% gold and **14** from the 90% red, so for a provider wearing coral the
threshold can't read as a change of hue. It reads as a change of *lightness*
instead — and that works unevenly:

| step | against coral | reads as |
| --- | --- | --- |
| coral → gold (70%) | 1.90:1 lighter | clear |
| coral → red (90%) | 1.07:1 | weak — the fill level and the figure carry it |

So at 90% a coral module leans on nine filled cells and the number rather than
on the colour. That is a deliberate trade for brand colour on the module you
look at every day; the label keeping its hue is what preserves identity when
the rest of the module has turned.

`tests/test_colours.py` pins all of it: every colour clears 4.5:1 on the bar
background, the three threshold colours are more than 40 degrees of hue apart
from each other, and the coral→gold and coral→red lightness steps are asserted
at their real values, so neither can drift unnoticed.

Note that hue distance, not contrast ratio, is the right measure between the
threshold colours: pine and red sit at 1.25:1 against each other and are
plainly blue and pink. For coral against them, lightness is the measure,
because the hues are already close.

## Two things the layout answers to

**GTK's tooltip ceiling.** GTK3 gives every tooltip a label with wrapping on
and `max-width-chars` fixed at 70, which no waybar setting and no CSS property
can reach. Go wider and GTK re-wraps the text at *its* chosen points. A
side-by-side layout does not survive that — the right-hand column folds into
the left one — which is why each provider gets its own module and its own
tooltip instead. Long prose is wrapped here, at that width, so the breaks land
between words at a sensible indent.

**Glyphs the bar font actually has.** `◉ ◎ △ ⌘ ◆ •` are all present in
JetBrains Mono at 0.600 em, the same advance as every other glyph. The GNOME
extension's `✱` and `✦` are *not* in it — they fall back to another face at an
unknown width. Hence the substitutions here. Check before adding one:

```sh
python3 -c "
from fontTools.ttLib import TTFont
f = TTFont('/usr/share/fonts/TTF/JetBrainsMonoNerdFont-Regular.ttf', fontNumber=0)
print(hex(0x25C9) in map(hex, f.getBestCmap()))"
```

The default hue is Rosé Pine **Moon**'s pine `#3e8fb0` rather than the base
variant's `#31748f`, which sits at 3.4:1 on the bar background — as dim as the
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
- Provider-supplied text is escaped into the markup, not interpreted as it —
  CodexBar titles windows things like `Fable only`, and a `&` in one would
  otherwise break the tooltip.
- Weekly windows that share a reset — Claude's all-models and per-model caps do
  — name it once in the `Weekly limits` heading instead of repeating the same
  line under each row. The session window keeps its own countdown, since "in
  4 min" is what matters for the window you are spending now.
- A provider that errors keeps its module and says `no data` with the reason,
  instead of the bar quietly showing another provider's number under its label.
- Only a spent quota fills all ten cells: rounding alone would draw 95% and
  100% identically, reading as exhausted while a twentieth is still there.
- A failed fetch keeps the last good reading, greyed out, with the reason in
  the tooltip — the same last-good behaviour CodexBar has upstream. A dash is
  reserved for having nothing to show at all.
- An earlier version of this installed a single combined `AI` module. Re-running
  the installer clears it. That mode still exists — run the script with no
  `--provider` and it shows the highest session percent across every provider,
  the same figure the GNOME panel indicator shows.
