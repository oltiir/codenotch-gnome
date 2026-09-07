#!/usr/bin/env bash
# Installs a prebuilt Codenotch COSMIC applet from the directory this script is
# in (a release tarball), or from cosmic/target/release after `cargo build`.
# Per-user, no sudo: ~/.local/bin, ~/.local/share/applications, icon theme dir.
set -euo pipefail

NAME=codenotch-cosmic
APPID=com.github.oltiir.Codenotch
HERE=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

BIN_DST=$HOME/.local/bin/$NAME
DESKTOP_DST=$HOME/.local/share/applications/$APPID.desktop
ICON_DST=$HOME/.local/share/icons/hicolor/scalable/apps/$APPID.svg

say() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
ok()  { printf '  \033[1;32mok\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mx\033[0m  %s\n' "$*" >&2; exit 1; }

# Prebuilt beside this script, else a local build.
if [ -x "$HERE/$NAME" ]; then
    BIN_SRC=$HERE/$NAME
    RES=$HERE
elif [ -x "$HERE/target/release/$NAME" ]; then
    BIN_SRC=$HERE/target/release/$NAME
    RES=$HERE/resources
else
    die "no $NAME binary next to this script or in target/release (run 'cargo build --release' or use a release tarball)"
fi
[ -f "$RES/$APPID.desktop" ] || die "missing $APPID.desktop beside the binary"
[ -f "$RES/icon.svg" ]       || die "missing icon.svg beside the binary"

say "Checking this binary runs here"
if ! "$BIN_SRC" --version >/dev/null 2>&1; then
    # No --version flag: fall back to loader check. A glibc mismatch shows here.
    if ldd "$BIN_SRC" 2>&1 | grep -qE "not found|GLIBC_"; then
        ldd "$BIN_SRC" | grep -E "not found|GLIBC_" | head -5
        die "this build won't run on this system (library mismatch above)"
    fi
fi
ok "loader is happy"

say "Installing the applet"
install -Dm0755 "$BIN_SRC" "$BIN_DST"
install -Dm0644 "$RES/$APPID.desktop" "$DESKTOP_DST"
# Absolute Exec so the panel finds it regardless of PATH.
sed -i "s|^Exec=.*|Exec=$BIN_DST|" "$DESKTOP_DST"
install -Dm0644 "$RES/icon.svg" "$ICON_DST"
ok "$BIN_DST"
ok "$DESKTOP_DST"
ok "$ICON_DST"

cat <<'EOF'

  Done. Add it to the panel:
    Settings -> Desktop -> Panel -> Configure panel applets -> Add applet -> Codenotch

  It reads from the local codexbar server, so run ./install.sh first if you
  haven't -- that sets up the CLI and the codexbar-serve user service.
EOF
