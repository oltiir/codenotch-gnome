#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
#
# Wire the codenotch module into an existing waybar config, and take it back
# out again. The config is a file the user owns and has customised, so this
# edits the text in place -- keeping their comments, key order and formatting
# -- rather than reserialising it, and it validates the result before handing
# anything back. Everything it writes sits between markers, which is what makes
# a re-run replace its own work instead of duplicating it.

import json
import re
import sys

PREFIX = "custom/codenotch"

# Mirrors the PROVIDERS map in codenotch-waybar. The server answers in whatever
# order it finishes in, so the bar orders them itself and stays put between
# installs. Anything not listed keeps the order it arrived in, after these.
ORDER = ["claude", "codex", "cursor", "copilot", "gemini"]
DEFAULT_PROVIDERS = ["claude", "codex"]


def in_order(providers):
    return sorted(dict.fromkeys(providers),
                  key=lambda p: (ORDER.index(p) if p in ORDER else len(ORDER),
                                 list(providers).index(p)))

BEGIN = "// >>> codenotch (managed by install-waybar.sh)"
END = "// <<< codenotch"

CSS_BEGIN = "/* >>> codenotch (managed by install-waybar.sh) */"
CSS_END = "/* <<< codenotch */"

# No "interval": the script runs continuously and paces its own fetches,
# because `codexbar serve` goes upstream on every /usage and a call costs
# seconds. Both modules share one cached reading, so a second one costs the
# server nothing. "restart-interval" is only a safety net if one ever dies.
MODULE = """  "%s": {
    "exec": "$HOME/.local/bin/codenotch-waybar --provider %s",
    "return-type": "json",
    "restart-interval": 30,
    "on-click": "omarchy-launch-floating-terminal-with-presentation codenotch-waybar --provider %s --print"
  },"""


def module_key(pid):
    return "%s-%s" % (PREFIX, pid)


def module_def(pid):
    return MODULE % (module_key(pid), pid, pid)


class PatchError(Exception):
    pass


def strip_comments(text):
    """JSONC to JSON. String-aware, so a // inside a URL stays put."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def parse(text):
    # NULs turn up in hand-edited configs, after the closing brace, and
    # waybar's own parser shrugs them off; refusing to patch over one would be
    # stricter than the thing being configured. Only the copy being validated
    # is cleaned -- the user's bytes are left as they are.
    try:
        return json.loads(strip_comments(text).replace("\x00", ""))
    except ValueError as e:
        raise PatchError("waybar config is not readable JSONC: %s" % e)


def unpatch(text):
    """Everything this script ever added, removed -- byte for byte, for any
    provider it was ever asked to install."""
    # The suffix is optional: an earlier version installed one combined module
    # called plain "custom/codenotch", and re-running has to clear that too.
    key = re.escape(PREFIX) + r"(?:-[a-z0-9_-]+)?"
    text = re.sub(r"\n[ \t]*" + re.escape(BEGIN) + r".*?" + re.escape(END),
                  "", text, flags=re.DOTALL)
    # As it goes in: ",\n    \"custom/codenotch-x\"" for a multi-line array,
    # ", \"custom/codenotch-x\"" for an inline one.
    text = re.sub(r",\n[ \t]*\"%s\"" % key, "", text)
    # An earlier version wrote the entry first in its array, so it owns its
    # line and the comma follows it. Take the line with it, or a blank one
    # with the old indentation is left behind.
    text = re.sub(r"\n[ \t]*\"%s\"," % key, "", text)
    text = re.sub(r",[ \t]*\"%s\"" % key, "", text)
    text = re.sub(r"\"%s\",[ \t]*" % key, "", text)
    text = re.sub(r"\"%s\"" % key, "", text)
    return text


def patch(text, providers=None):
    providers = in_order(providers or DEFAULT_PROVIDERS)
    config = parse(text)
    if "modules-left" not in config:
        raise PatchError('no "modules-left" in the waybar config -- '
                         "add the modules by hand, or reset with "
                         "`omarchy refresh waybar` first")

    # Start from a clean config so a re-run replaces rather than stacks.
    text = unpatch(text)

    defs = "\n".join(module_def(p) for p in providers)
    opening = text.index("{")
    if text[opening + 1:opening + 2] not in ("\n", ""):
        # The END marker is a // comment, so it needs the rest of its line to
        # itself; here that line already holds the first key.
        raise PatchError("this config keeps its first key on the same line as "
                         "the opening brace, and the marker comment needs its "
                         "own line -- put the first key on a line of its own, "
                         "or add the modules by hand")
    text = "%s\n  %s\n%s\n  %s%s" % (
        text[:opening + 1], BEGIN, defs, END, text[opening + 1:])

    text = _join_modules_left(text, providers)

    after = parse(text)
    keys = [module_key(p) for p in providers]
    if after["modules-left"][-len(keys):] != keys or not all(k in after for k in keys):
        raise PatchError("patched config did not come out as expected")
    return text


def _join_modules_left(text, providers):
    """Append to the tail of the left-hand group, in whichever array style the
    config uses. The tail, not the head: that is where the bar has room."""
    m = re.search(r"\"modules-left\"\s*:\s*\[", text)
    if not m:
        raise PatchError('no "modules-left" array found in the config text')

    close = _closing_bracket(text, m.end() - 1)
    inner = text[m.end():close]
    if not inner.strip():
        raise PatchError('"modules-left" is empty -- nothing to append to')

    # Insert straight after the last entry, so unpatch can lift it back out.
    tail = len(inner) - len(inner.rstrip())
    at = close - tail

    if "\n" in inner:
        indent = re.findall(r"\n([ \t]*)\S", inner)
        pad = indent[-1] if indent else "    "
        added = "".join(',\n%s"%s"' % (pad, module_key(p)) for p in providers)
    else:
        added = "".join(', "%s"' % module_key(p) for p in providers)
    return text[:at] + added + text[at:]


def _closing_bracket(text, at):
    """The ] matching the [ at `at`, skipping any inside strings."""
    depth = 0
    i = at
    while i < len(text):
        c = text[i]
        if c == '"':
            i += 1
            while i < len(text) and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif c == "[":
            depth += 1
        elif c == "]":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise PatchError('unterminated "modules-left" array')


def patch_css(css, block):
    css = unpatch_css(css)
    return "%s\n%s\n%s\n%s\n" % (css, CSS_BEGIN, block.strip(), CSS_END)


def unpatch_css(css):
    return re.sub(r"\n" + re.escape(CSS_BEGIN) + r".*?" + re.escape(CSS_END) + r"\n",
                  "", css, flags=re.DOTALL)


def main(argv):
    """patch-config.py <patch|unpatch> <config.jsonc> [<style.css> <block.css> [<providers>]]

    <providers> is a comma-separated list, e.g. claude,codex."""
    if len(argv) < 2 or argv[0] not in ("patch", "unpatch"):
        print(main.__doc__, file=sys.stderr)
        return 2

    action, config_path = argv[0], argv[1]
    providers = argv[4].split(",") if len(argv) >= 5 and argv[4] else None
    apply = (lambda t: patch(t, providers)) if action == "patch" else unpatch
    apply_css = patch_css if action == "patch" else (lambda css, _b: unpatch_css(css))

    try:
        with open(config_path) as f:
            before = f.read()
        after = apply(before)
        if after != before:
            with open(config_path, "w") as f:
                f.write(after)

        if len(argv) >= 3:
            css_path = argv[2]
            block = open(argv[3]).read() if len(argv) >= 4 else ""
            with open(css_path) as f:
                before_css = f.read()
            after_css = apply_css(before_css, block)
            if after_css != before_css:
                with open(css_path, "w") as f:
                    f.write(after_css)
    except PatchError as e:
        print("codenotch: %s" % e, file=sys.stderr)
        return 1
    except OSError as e:
        print("codenotch: %s" % e, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
