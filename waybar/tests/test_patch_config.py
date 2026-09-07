"""Wiring the module into an existing waybar config. This edits a file the user
owns and has customised, so it has to be reversible, idempotent, and never
leave a config waybar can't read."""

import json
import unittest

from support import load_patcher, read_fixture

pc = load_patcher()

STOCK = read_fixture("omarchy-stock.jsonc")

# Both array styles waybar configs use in the wild.
INLINE = """{
  // my bar
  "modules-left": ["custom/omarchy", "clock"],
  "modules-right": ["cpu", "battery"],
  "cpu": {"format": "CPU"}
}
"""


class StripComments(unittest.TestCase):
    """Comments have to come out to validate the result, but only real ones."""

    def test_line_and_block_comments_are_removed(self):
        text = '{"a": 1, // trailing\n /* block\n spanning */ "b": 2}'
        self.assertEqual(json.loads(pc.strip_comments(text)), {"a": 1, "b": 2})

    def test_comment_markers_inside_strings_survive(self):
        text = '{"url": "http://127.0.0.1:8787/usage", "x": "/* not a comment */"}'
        self.assertEqual(json.loads(pc.strip_comments(text))["url"],
                         "http://127.0.0.1:8787/usage")

    def test_an_escaped_quote_does_not_end_the_string(self):
        text = '{"a": "say \\" // still a string", "b": 2}'
        self.assertEqual(json.loads(pc.strip_comments(text))["b"], 2)


class Patch(unittest.TestCase):
    """One module per provider, joining the end of the left-hand group -- the
    bar's right-hand group has ~15px of slack, and the space is on the left."""

    def test_a_module_lands_at_the_end_of_the_left_hand_group(self):
        for name, text in (("stock", STOCK), ("inline", INLINE)):
            with self.subTest(config=name):
                got = json.loads(pc.strip_comments(pc.patch(text)))
                self.assertEqual(got["modules-left"][-2:],
                                 ["custom/codenotch-claude", "custom/codenotch-codex"])

    def test_the_rest_of_the_left_hand_group_keeps_its_order(self):
        before = json.loads(pc.strip_comments(STOCK))["modules-left"]
        after = json.loads(pc.strip_comments(pc.patch(STOCK)))["modules-left"]
        self.assertEqual(after[:len(before)], before)

    def test_the_right_hand_group_is_left_alone(self):
        before = json.loads(pc.strip_comments(STOCK))["modules-right"]
        after = json.loads(pc.strip_comments(pc.patch(STOCK)))["modules-right"]
        self.assertEqual(after, before)

    def test_each_module_asks_for_its_own_provider(self):
        got = json.loads(pc.strip_comments(pc.patch(STOCK)))
        for pid in ("claude", "codex"):
            with self.subTest(provider=pid):
                module = got["custom/codenotch-%s" % pid]
                self.assertEqual(module["return-type"], "json")
                self.assertIn("--provider %s" % pid, module["exec"])
                self.assertIn("--provider %s" % pid, module["on-click"])

    def test_the_modules_go_in_a_fixed_order_whatever_order_they_arrive_in(self):
        # The server answers in whatever order it finishes in, which would
        # otherwise shuffle the bar between installs.
        got = json.loads(pc.strip_comments(pc.patch(STOCK, ["codex", "claude"])))
        self.assertEqual(got["modules-left"][-2:],
                         ["custom/codenotch-claude", "custom/codenotch-codex"])

    def test_a_provider_it_has_never_heard_of_goes_last(self):
        got = json.loads(pc.strip_comments(pc.patch(STOCK, ["mistral", "claude"])))
        self.assertEqual(got["modules-left"][-2:],
                         ["custom/codenotch-claude", "custom/codenotch-mistral"])

    def test_the_providers_installed_are_configurable(self):
        got = json.loads(pc.strip_comments(pc.patch(STOCK, ["claude"])))
        self.assertIn("custom/codenotch-claude", got)
        self.assertNotIn("custom/codenotch-codex", got)
        self.assertEqual(got["modules-left"][-1], "custom/codenotch-claude")

    def test_the_definition_has_no_interval_because_the_script_paces_itself(self):
        got = json.loads(pc.strip_comments(pc.patch(STOCK)))
        self.assertNotIn("interval", got["custom/codenotch-claude"])

    def test_the_patched_config_is_still_readable(self):
        for name, text in (("stock", STOCK), ("inline", INLINE)):
            with self.subTest(config=name):
                json.loads(pc.strip_comments(pc.patch(text)))  # must not raise

    def test_everything_the_user_already_had_is_still_there(self):
        before = json.loads(pc.strip_comments(STOCK))
        after = json.loads(pc.strip_comments(pc.patch(STOCK)))
        for key, value in before.items():
            with self.subTest(key=key):
                if key != "modules-left":
                    self.assertEqual(after[key], value)

    def test_the_users_own_comments_are_left_alone(self):
        self.assertIn("// my bar", pc.patch(INLINE))

    def test_patching_twice_changes_nothing_the_second_time(self):
        once = pc.patch(STOCK)
        self.assertEqual(pc.patch(once), once)

    def test_patching_twice_does_not_list_the_modules_twice(self):
        twice = json.loads(pc.strip_comments(pc.patch(pc.patch(STOCK))))
        for pid in ("claude", "codex"):
            with self.subTest(provider=pid):
                self.assertEqual(
                    twice["modules-left"].count("custom/codenotch-%s" % pid), 1)

    def test_changing_the_provider_list_replaces_the_old_modules(self):
        one = json.loads(pc.strip_comments(pc.patch(pc.patch(STOCK), ["claude"])))
        self.assertNotIn("custom/codenotch-codex", one)
        self.assertNotIn("custom/codenotch-codex", one["modules-left"])

    def test_a_stray_nul_byte_does_not_block_the_patch(self):
        # Seen in the wild: an editor leaves a NUL after the closing brace.
        # Waybar's own parser shrugs it off, so refusing to patch would be
        # stricter than the thing being configured.
        text = STOCK.rstrip("\n") + "\n\x00\n"
        got = json.loads(pc.strip_comments(pc.patch(text)).replace("\x00", ""))
        self.assertEqual(got["modules-left"][-1], "custom/codenotch-codex")

    def test_a_config_with_no_left_hand_group_is_refused(self):
        with self.assertRaises(pc.PatchError):
            pc.patch('{"modules-right": ["clock"]}')

    def test_an_empty_left_hand_group_is_refused(self):
        with self.assertRaises(pc.PatchError):
            pc.patch('{\n  "modules-left": []\n}')

    def test_a_config_written_all_on_one_line_is_refused_not_mangled(self):
        # The marker comment would swallow whatever follows it on that line, so
        # this refuses rather than writing a config waybar can't read.
        with self.assertRaises(pc.PatchError) as caught:
            pc.patch('{"modules-left": ["clock"], "clock": {}}')
        self.assertIn("its own line", str(caught.exception))

    def test_a_config_that_cannot_be_read_is_refused(self):
        with self.assertRaises(pc.PatchError):
            pc.patch('{"modules-left": ["cpu",')


class Migration(unittest.TestCase):
    """An earlier version installed one combined module called plain
    "custom/codenotch". Re-running has to clear that too, or waybar is left
    listing a module nothing defines."""

    OLD = """{
  // >>> codenotch (managed by install-waybar.sh)
  "custom/codenotch": {
    "exec": "$HOME/.local/bin/codenotch-waybar",
    "return-type": "json"
  },
  // <<< codenotch
  "modules-left": ["custom/omarchy", "clock"],
  "modules-right": [
    "custom/codenotch",
    "cpu"
  ]
}
"""

    def test_the_old_combined_module_is_cleared_on_re_patch(self):
        got = json.loads(pc.strip_comments(pc.patch(self.OLD)))
        self.assertNotIn("custom/codenotch", got)
        self.assertNotIn("custom/codenotch", got["modules-right"])
        self.assertEqual(got["modules-right"], ["cpu"])

    def test_and_the_new_modules_take_its_place(self):
        got = json.loads(pc.strip_comments(pc.patch(self.OLD)))
        self.assertEqual(got["modules-left"][-2:],
                         ["custom/codenotch-claude", "custom/codenotch-codex"])

    OLD_CLEAN = """{
  "modules-left": ["custom/omarchy", "clock"],
  "modules-right": [
    "cpu"
  ]
}
"""

    def test_unpatching_the_old_layout_restores_it_exactly(self):
        # Not just "no codenotch left": removing the entry has to take its
        # line with it, or a whitespace-only line is left where it was.
        self.assertEqual(pc.unpatch(self.OLD), self.OLD_CLEAN)


class Unpatch(unittest.TestCase):
    def test_removing_the_module_restores_the_original_exactly(self):
        for name, text in (("stock", STOCK), ("inline", INLINE)):
            with self.subTest(config=name):
                self.assertEqual(pc.unpatch(pc.patch(text)), text)

    def test_removing_from_an_unpatched_config_changes_nothing(self):
        self.assertEqual(pc.unpatch(STOCK), STOCK)


class Stylesheet(unittest.TestCase):
    CSS = "@import './rose-pine.css';\n#cpu { color: @rose; }\n"
    BLOCK = "#custom-codenotch { color: @codenotch; }\n"

    def test_the_block_is_appended(self):
        out = pc.patch_css(self.CSS, self.BLOCK)
        self.assertTrue(out.startswith(self.CSS))
        self.assertIn("#custom-codenotch", out)

    def test_appending_twice_appends_once(self):
        once = pc.patch_css(self.CSS, self.BLOCK)
        self.assertEqual(pc.patch_css(once, self.BLOCK), once)

    def test_a_changed_block_replaces_the_old_one(self):
        once = pc.patch_css(self.CSS, self.BLOCK)
        twice = pc.patch_css(once, "#custom-codenotch { color: @gold; }\n")
        self.assertIn("@gold", twice)
        self.assertNotIn("@codenotch", twice)

    def test_removing_the_block_restores_the_original(self):
        self.assertEqual(pc.unpatch_css(pc.patch_css(self.CSS, self.BLOCK)), self.CSS)


if __name__ == "__main__":
    unittest.main()
