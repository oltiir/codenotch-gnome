#!/usr/bin/env bash
# Codenotch installer: CodexBar CLI + usage server + GNOME extension.
# Idempotent -- safe to re-run. Touches only ~/.local, ~/.config and dconf.
set -euo pipefail

CODEXBAR_REPO=${CODEXBAR_REPO:-steipete/CodexBar}
UUID=codenotch@oltiir.github.io
PORT=${PORT:-8787}

LIBEXEC=$HOME/.local/libexec/codexbar
BINDIR=$HOME/.local/bin
EXTDIR=$HOME/.local/share/gnome-shell/extensions
UNITDIR=$HOME/.config/systemd/user
SRC=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

say()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
ok()   { printf '  \033[1;32mok\033[0m %s\n' "$*"; }
warn() { printf '  \033[1;33m!\033[0m  %s\n' "$*"; }
die()  { printf '\033[1;31mx\033[0m  %s\n' "$*" >&2; exit 1; }

# --- preflight -------------------------------------------------------------
say "Checking this machine"

for tool in curl tar python3 systemctl; do
    command -v "$tool" >/dev/null || die "missing required tool: $tool"
done

HAVE_GNOME=no
if command -v gnome-shell >/dev/null; then
    HAVE_GNOME=yes
    command -v gsettings >/dev/null || die "missing required tool: gsettings"
    # The COSMIC release tarball ships this script without the extension.
    [ -d "$SRC/$UUID" ] || die "no $UUID/ beside the script -- run this from the repo checkout on GNOME"
    SHELL_VER=$(gnome-shell --version | grep -oE '[0-9]+' | head -1)
    SUPPORTED=$(python3 -c "
import json,sys
m=json.load(open('$SRC/$UUID/metadata.json'))
print('yes' if '$SHELL_VER' in m['shell-version'] else 'no')")
    [ "$SUPPORTED" = yes ] \
        && ok "GNOME Shell $SHELL_VER (supported)" \
        || warn "GNOME Shell $SHELL_VER is not in metadata.json shell-version; it may refuse to load"
else
    warn "no GNOME Shell here -- will set up the CLI and server only"
    warn "for COSMIC / Pop!_OS 24.04 install the applet afterwards (see below)"
fi

case "$(uname -m)" in
    x86_64)  ARCH=linux-x86_64  ;;
    aarch64) ARCH=linux-aarch64 ;;
    *)       die "unsupported architecture: $(uname -m)" ;;
esac
ok "architecture $ARCH"

# --- 1. CodexBar CLI -------------------------------------------------------
say "Installing the CodexBar CLI"

TAG=${CODEXBAR_TAG:-$(curl -fsSL "https://api.github.com/repos/$CODEXBAR_REPO/releases/latest" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"])')}
[ -n "$TAG" ] || die "could not resolve the latest CodexBar release"

if [ -x "$LIBEXEC/CodexBarCLI" ] && [ "$(cat "$LIBEXEC/VERSION" 2>/dev/null)" = "${TAG#v}" ]; then
    ok "already at $TAG"
else
    TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
    ASSET=CodexBarCLI-$TAG-$ARCH.tar.gz
    say "  downloading $ASSET"
    BASE=https://github.com/$CODEXBAR_REPO/releases/download/$TAG
    curl -fSL --progress-bar -o "$TMP/$ASSET"        "$BASE/$ASSET"
    curl -fsSL              -o "$TMP/$ASSET.sha256"  "$BASE/$ASSET.sha256"

    ( cd "$TMP" && sha256sum -c "$ASSET.sha256" >/dev/null ) \
        || die "checksum mismatch on $ASSET -- refusing to install"
    ok "checksum verified"

    tar -xzf "$TMP/$ASSET" -C "$TMP"
    mkdir -p "$LIBEXEC" "$BINDIR"

    # An already-running `codexbar serve` keeps the old binary mapped, so a
    # straight copy over it fails with "Text file busy". Stop the service and
    # swap the binary in by rename; step 3 starts it again.
    systemctl --user stop codexbar-serve >/dev/null 2>&1 || true

    # The tarball ships a plugin bundle that must sit beside the binary, so the
    # whole payload goes in libexec and only the entrypoint is symlinked.
    rm -rf "$LIBEXEC/CodexBar_CodexBarCore.bundle"
    cp -r "$TMP/CodexBar_CodexBarCore.bundle" "$LIBEXEC/"
    cp "$TMP/CodexBarCLI" "$LIBEXEC/CodexBarCLI.new"
    chmod 755 "$LIBEXEC/CodexBarCLI.new"
    mv -f "$LIBEXEC/CodexBarCLI.new" "$LIBEXEC/CodexBarCLI"
    cp "$TMP/VERSION" "$LIBEXEC/VERSION"

    ln -sfn "$LIBEXEC/CodexBarCLI" "$BINDIR/codexbar"
    ok "installed to $LIBEXEC, symlinked as $BINDIR/codexbar"
fi

cb() { "$BINDIR/codexbar" "$@" 2>&1 | grep -v 'no version information available' || true; }
[ -n "$(cb --version)" ] || die "codexbar will not run; try CODEXBAR_TAG=$TAG with the musl tarball"
ok "$(cb --version)"

case ":$PATH:" in
    *":$BINDIR:"*) ;;
    *) warn "$BINDIR is not on your PATH -- add it so 'codexbar' works in a shell" ;;
esac

# --- 2. Providers ----------------------------------------------------------
say "Enabling providers that are actually signed in here"

enable_if() {  # name, credential path or command
    local id=$1 probe=$2
    if [ -e "$probe" ] || command -v "$probe" >/dev/null 2>&1; then
        cb config enable --provider "$id" >/dev/null
        ok "$id enabled"
    else
        cb config disable --provider "$id" >/dev/null
        warn "$id not signed in here -- disabled (it would show 'no data')"
    fi
}
enable_if claude "$HOME/.claude/.credentials.json"
enable_if codex  codex

if ! cb --provider all --format json | grep -q '"usage"'; then
    warn "no provider returned usage yet; the panel will show an error glyph"
    warn "check one directly: codexbar --provider claude --format json -v"
fi

# --- 3. Usage server -------------------------------------------------------
say "Starting the localhost usage server"

mkdir -p "$UNITDIR"
install -m644 "$SRC/systemd/codexbar-serve.service" "$UNITDIR/codexbar-serve.service"
systemctl --user daemon-reload
systemctl --user enable --now codexbar-serve >/dev/null 2>&1 || true
systemctl --user restart codexbar-serve

for _ in $(seq 20); do
    HEALTH=$(curl -fsS --max-time 5 "localhost:$PORT/health" 2>/dev/null) && break
    sleep 1
done
[ -n "${HEALTH:-}" ] \
    || die "server did not answer on :$PORT -- systemctl --user status codexbar-serve"
ok "serving on 127.0.0.1:$PORT  $HEALTH"

USAGE=$(curl -fsS --max-time 45 "localhost:$PORT/usage" 2>/dev/null || true)
python3 - "$USAGE" <<'PY' || true
import json, sys
try:
    rows = json.loads(sys.argv[1] or "[]")
except Exception:
    sys.exit(0)
for r in rows:
    u = (r.get("usage") or {}).get("primary") or {}
    if "usedPercent" in u:
        print(f"  \033[1;32mok\033[0m {r['provider']}: {u['usedPercent']}% used")
    else:
        msg = (r.get("error") or {}).get("message", "no data")
        print(f"  \033[1;33m!\033[0m  {r['provider']}: {msg}")
PY

# --- 4. Extension ----------------------------------------------------------
if [ "$HAVE_GNOME" = no ]; then
    say "Skipping the GNOME extension (no GNOME Shell)"
    cat <<'EOF'

  Done. The CodexBar CLI and usage server are installed and start at login.

  On COSMIC, install the applet next -- prebuilt from a release tarball:
    ./install-applet.sh
  or built from the repo checkout:
    cd cosmic && just install
  then Settings -> Desktop -> Panel -> Configure panel applets -> Add applet -> Codenotch
EOF
    exit 0
fi

say "Installing the GNOME extension"

# Must be gnome-shell/extensions; the shell silently ignores gnome-extensions/.
mkdir -p "$EXTDIR"
rm -rf "$EXTDIR/$UUID"
cp -r "$SRC/$UUID" "$EXTDIR/"
ok "installed to $EXTDIR/$UUID"

python3 - <<PY
import subprocess, ast
KEY = ["gsettings", "get", "org.gnome.shell", "enabled-extensions"]
cur = subprocess.run(KEY, capture_output=True, text=True).stdout.strip()
try:
    lst = ast.literal_eval(cur)
    if not isinstance(lst, list):
        lst = []
except (ValueError, SyntaxError):
    lst = []          # "@as []" on a pristine profile
if "$UUID" in lst:
    print("  \033[1;32mok\033[0m already in enabled-extensions")
else:
    lst.append("$UUID")
    val = "[" + ", ".join("'%s'" % x for x in lst) + "]"
    subprocess.run(["gsettings", "set", "org.gnome.shell",
                    "enabled-extensions", val], check=True)
    print("  \033[1;32mok\033[0m added to enabled-extensions (%d total)" % len(lst))
PY

if [ "$(gsettings get org.gnome.shell disable-user-extensions)" = true ]; then
    warn "user extensions are globally disabled; re-enable with:"
    warn "  gsettings set org.gnome.shell disable-user-extensions false"
fi

# --- done ------------------------------------------------------------------
cat <<'EOF'

  Done. One step left: log out and log back in.

  Wayland cannot load an extension into a running shell, so the panel will
  not show it until the session restarts. Both pieces then start on their
  own at every login.

  After logging back in:
    gnome-extensions info codenotch@oltiir.github.io   # State: ACTIVE
EOF
