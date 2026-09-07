# The module under test is an executable without a .py suffix, the way it
# lands in ~/.local/bin, so it has to be loaded by path rather than imported.
import importlib.util
import json
import pathlib
import sys
from importlib.machinery import SourceFileLoader

HERE = pathlib.Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
SCRIPT = HERE.parent / "codenotch-waybar"


def load():
    # No .py suffix, so the loader has to be named rather than inferred.
    loader = SourceFileLoader("codenotch_waybar", str(SCRIPT))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["codenotch_waybar"] = mod
    spec.loader.exec_module(mod)
    return mod


def fixture(name):
    return json.loads((FIXTURES / name).read_text())


def claude_entry():
    return next(e for e in fixture("live.json") if e["provider"] == "claude")


def codex_entry():
    return next(e for e in fixture("live.json") if e["provider"] == "codex")


def strip_markup(s):
    """Pango markup out, visible text in -- so assertions read like the
    tooltip looks. Test-only; the script never needs it."""
    import html
    import re
    return html.unescape(re.sub(r"<[^>]+>", "", s))


def load_patcher():
    import importlib.util
    path = HERE.parent / "patch-config.py"
    spec = importlib.util.spec_from_file_location("patch_config", path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["patch_config"] = mod
    spec.loader.exec_module(mod)
    return mod


def read_fixture(name):
    return (FIXTURES / name).read_text()
