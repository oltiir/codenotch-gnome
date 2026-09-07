#!/usr/bin/env bash
# Codenotch for waybar: the custom module, wired into an existing bar.
# Idempotent -- safe to re-run, and it's also the upgrade path. Touches only
# ~/.local and ~/.config, no sudo. `--uninstall` puts everything back.
set -euo pipefail

PORT=${PORT:-8787}
SRC=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

BINDIR=$HOME/.local/bin
LIBDIR=$HOME/.local/libexec/codenotch
CONFDIR=${WAYBAR_CONFIG_DIR:-$HOME/.config/waybar}
CONFIG=$CONFDIR/config.jsonc
STYLE=$CONFDIR/style.css

say()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
ok()   { printf '  \033[1;32mok\033[0m %s\n' "$*"; }
warn() { printf '  \033[1;33m!\033[0m  %s\n' "$*"; }
die()  { printf '\033[1;31mx\033[0m  %s\n' "$*" >&2; exit 1; }

# The patcher lives beside this script in a checkout, and in libexec once
# installed, so an uninstall works without the repo still being around.
patcher() {
    if [ -f "$SRC/patch-config.py" ]; then echo "$SRC/patch-config.py"
    elif [ -f "$LIBDIR/patch-config.py" ]; then echo "$LIBDIR/patch-config.py"
    else die "patch-config.py not found beside this script or in $LIBDIR"; fi
}

restart_waybar() {
    if command -v omarchy >/dev/null; then
        omarchy restart waybar >/dev/null 2>&1 && { ok "waybar restarted"; return; }
    fi
    # Waybar reloads its config on SIGUSR2, which is enough off Omarchy.
    if pkill -SIGUSR2 -x waybar 2>/dev/null; then
        ok "waybar reloaded (SIGUSR2)"
    else
        warn "waybar isn't running -- start it to see the module"
    fi
}

backup() {  # path
    [ -f "$1" ] || return 0
    cp -p "$1" "$1.bak.$(date +%s)"
}

# --- uninstall -------------------------------------------------------------
if [ "${1:-}" = "--uninstall" ]; then
    say "Removing the waybar module"
    [ -f "$CONFIG" ] && backup "$CONFIG"
    [ -f "$STYLE" ] && backup "$STYLE"
    if [ -f "$CONFIG" ]; then
        python3 "$(patcher)" unpatch "$CONFIG" "$STYLE"
        ok "config.jsonc and style.css put back"
    fi
    rm -f "$BINDIR/codenotch-waybar"
    rm -rf "$LIBDIR"
    rm -f "${XDG_RUNTIME_DIR:-/tmp}/codenotch-waybar.json"
    ok "removed $BINDIR/codenotch-waybar"
    restart_waybar
    cat <<'MSG'

  The module is gone. The CodexBar CLI and server are untouched; drop those with
    systemctl --user disable --now codexbar-serve
    rm -f ~/.config/systemd/user/codexbar-serve.service
    rm -rf ~/.local/libexec/codexbar ~/.local/bin/codexbar
MSG
    exit 0
fi

# --- preflight -------------------------------------------------------------
say "Checking this machine"

for tool in python3 curl; do
    command -v "$tool" >/dev/null || die "missing required tool: $tool"
done
command -v waybar >/dev/null || warn "waybar is not on PATH -- installing anyway"
[ -f "$CONFIG" ] || die "no waybar config at $CONFIG -- set WAYBAR_CONFIG_DIR if it lives elsewhere"
[ -f "$STYLE" ] || die "no stylesheet at $STYLE"
ok "waybar config at $CONFDIR"

if ! curl -fsS --max-time 5 "localhost:$PORT/health" >/dev/null 2>&1; then
    die "nothing answering on localhost:$PORT -- run ./install.sh first (it installs
     the CodexBar CLI and the usage server these modules read)"
fi
ok "usage server answering on 127.0.0.1:$PORT"

# One module per provider, so ask the server which ones it actually reports.
# This call goes upstream and takes as long as the slowest provider does.
say "Asking which providers are enabled"
USAGE=$(curl -fsS --max-time 60 "localhost:$PORT/usage" 2>/dev/null || true)
PROVIDERS=$(python3 - "$USAGE" <<'PY'
import json, sys
try:
    rows = json.loads(sys.argv[1] or "[]")
except ValueError:
    rows = []
ids = [r["provider"] for r in rows if isinstance(r, dict) and r.get("provider")]
print(",".join(dict.fromkeys(ids)))
PY
)
if [ -z "$PROVIDERS" ]; then
    PROVIDERS=claude,codex
    warn "the server named no providers -- installing $PROVIDERS, which will read"
    warn "'not enabled' until you sign in and re-run this"
else
    ok "providers: $PROVIDERS"
fi

# --- 1. the module ---------------------------------------------------------
say "Installing the module"

mkdir -p "$BINDIR" "$LIBDIR"
install -m755 "$SRC/codenotch-waybar" "$BINDIR/codenotch-waybar"
install -m644 "$SRC/patch-config.py" "$LIBDIR/patch-config.py"
ok "installed $BINDIR/codenotch-waybar"

case ":$PATH:" in
    *":$BINDIR:"*) ;;
    *) warn "$BINDIR is not on your PATH -- the module uses an absolute path, so"
       warn "the bar works either way, but 'codenotch-waybar --print' won't" ;;
esac

# --- 2. the bar ------------------------------------------------------------
say "Wiring it into the bar"

backup "$CONFIG"
backup "$STYLE"
# modules-left, at the tail: a full bar has its slack on the left, not beside
# the right-hand group, where a second module has nowhere to go.
python3 "$(patcher)" patch "$CONFIG" "$STYLE" "$SRC/style.css" "$PROVIDERS"
ok "one module per provider added to the end of modules-left, styles appended"

restart_waybar

# --- 3. did it work? -------------------------------------------------------
say "Reading each quota once, the way the bar will"

READINGS=""
OLD_IFS=$IFS; IFS=,
for prov in $PROVIDERS; do
    IFS=$OLD_IFS
    READINGS="$READINGS$("$BINDIR/codenotch-waybar" --once --provider "$prov" 2>/dev/null || true)
"
    IFS=,
done
IFS=$OLD_IFS

python3 - "$READINGS" <<'PY' || true
import html, json, re, sys
stale = False
for line in (sys.argv[1] or "").splitlines():
    if not line.strip():
        continue
    try:
        p = json.loads(line)
    except ValueError:
        continue
    text = " ".join(re.sub(r"<[^>]+>", "", p.get("text", "")).split())
    tip = html.unescape(re.sub(r"<[^>]+>", "", p.get("tooltip", "")))
    if not text:
        print("  \033[1;33m!\033[0m  no reading")
    elif p.get("class") == "stale":
        # Say why, or the grey dash is a mystery to debug later.
        why = next((l.strip() for l in reversed(tip.splitlines()) if l.strip()), "")
        print("  \033[1;33m!\033[0m  %s -- greyed out: %s" % (text, why))
        stale = True
    else:
        print("  \033[1;32mok\033[0m %s" % text)
if stale:
    print("     a greyed module retries every couple of minutes; check the server")
    print("     with: systemctl --user status codexbar-serve")
PY

cat <<'MSG'

  Done -- no logout needed, unlike the GNOME extension.

  Each module has its own tooltip -- hover Claude, get only Claude. Click one
  for its full breakdown in a floating terminal. To see what the bar is fed:
    codenotch-waybar --once --provider claude    # the raw waybar JSON
    codenotch-waybar --print --provider claude   # the same, as plain text

  Uninstall with ./install-waybar.sh --uninstall
MSG
