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

MODULE_KEY = "custom/codenotch"
BEGIN = "// >>> codenotch (managed by install-waybar.sh)"
END = "// <<< codenotch"

CSS_BEGIN = "/* >>> codenotch (managed by install-waybar.sh) */"
CSS_END = "/* <<< codenotch */"

# No "interval": the script runs continuously and paces its own fetches,
# because `codexbar serve` goes upstream on every /usage and a call costs
# seconds. "restart-interval" is only a safety net if it ever dies.
MODULE = """  "%s": {
    "exec": "$HOME/.local/bin/codenotch-waybar",
    "return-type": "json",
    "restart-interval": 30,
    "on-click": "omarchy-launch-floating-terminal-with-presentation codenotch-waybar --print"
  },""" % MODULE_KEY


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
    """Everything this script ever added, removed -- byte for byte."""
    text = re.sub(r"\n[ \t]*" + re.escape(BEGIN) + r".*?" + re.escape(END),
                  "", text, flags=re.DOTALL)
    text = re.sub(r"\n[ \t]*\"%s\",?" % re.escape(MODULE_KEY), "", text)
    text = re.sub(r"\"%s\",[ \t]*" % re.escape(MODULE_KEY), "", text)
    text = re.sub(r"\"%s\"" % re.escape(MODULE_KEY), "", text)
    return text


def patch(text):
    config = parse(text)
    if "modules-right" not in config:
        raise PatchError('no "modules-right" in the waybar config -- '
                         "add the module by hand, or reset with "
                         "`omarchy refresh waybar` first")

    # Start from a clean config so a re-run replaces rather than stacks.
    text = unpatch(text)

    opening = text.index("{")
    text = "%s\n  %s\n%s\n  %s%s" % (
        text[:opening + 1], BEGIN, MODULE, END, text[opening + 1:])

    text = _join_modules_right(text)

    after = parse(text)
    if after["modules-right"][0] != MODULE_KEY or MODULE_KEY not in after:
        raise PatchError("patched config did not come out as expected")
    return text


def _join_modules_right(text):
    """Put the module at the head of the right-hand group, in whichever array
    style the config uses."""
    m = re.search(r"\"modules-right\"\s*:\s*\[", text)
    if not m:
        raise PatchError('no "modules-right" array found in the config text')

    at = m.end()
    rest = text[at:]
    empty = rest.lstrip().startswith("]")
    entry = '"%s"' % MODULE_KEY if empty else '"%s",' % MODULE_KEY

    if rest.lstrip(" \t").startswith("\n"):
        # Multi-line array: match the indentation of the entries already there.
        indent = re.match(r"\s*\n([ \t]*)", rest)
        pad = indent.group(1) if indent else "    "
        return "%s\n%s%s%s" % (text[:at], pad, entry, rest)
    return "%s%s %s" % (text[:at], entry, rest)


def patch_css(css, block):
    css = unpatch_css(css)
    return "%s\n%s\n%s\n%s\n" % (css, CSS_BEGIN, block.strip(), CSS_END)


def unpatch_css(css):
    return re.sub(r"\n" + re.escape(CSS_BEGIN) + r".*?" + re.escape(CSS_END) + r"\n",
                  "", css, flags=re.DOTALL)


def main(argv):
    """patch-config.py <patch|unpatch> <config.jsonc> [<style.css> <block.css>]"""
    if len(argv) < 2 or argv[0] not in ("patch", "unpatch"):
        print(main.__doc__, file=sys.stderr)
        return 2

    action, config_path = argv[0], argv[1]
    apply = patch if action == "patch" else unpatch
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
