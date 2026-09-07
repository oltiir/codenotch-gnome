"""Envelope and rate-window parsing: mirrors providersFrom() and windowsFrom()
in extension.js so all three front-ends read the same JSON the same way."""

import unittest

from support import claude_entry, codex_entry, load

cn = load()


class ProvidersFrom(unittest.TestCase):
    """`codexbar serve` mirrors the CLI, which emits one object for a single
    provider and a collection for several. Be liberal about the envelope."""

    def test_bare_array_is_the_provider_list(self):
        self.assertEqual(cn.providers_from([{"provider": "claude"}]),
                         [{"provider": "claude"}])

    def test_finds_the_list_under_a_wrapper_key(self):
        for key in ("providers", "usages", "results", "items"):
            with self.subTest(key=key):
                self.assertEqual(cn.providers_from({key: [{"provider": "claude"}]}),
                                 [{"provider": "claude"}])

    def test_a_single_provider_object_becomes_a_one_entry_list(self):
        entry = {"provider": "claude", "usage": {}}
        self.assertEqual(cn.providers_from(entry), [entry])

    def test_junk_yields_no_providers(self):
        for junk in (None, "nope", {"unrelated": 1}, 7):
            with self.subTest(junk=junk):
                self.assertEqual(cn.providers_from(junk), [])


class ClampPct(unittest.TestCase):
    def test_percentages_are_rounded_into_zero_to_one_hundred(self):
        self.assertEqual(cn.clamp_pct(11.6), 12)
        self.assertEqual(cn.clamp_pct(-5), 0)
        self.assertEqual(cn.clamp_pct(140), 100)

    def test_a_non_number_is_not_a_percentage(self):
        self.assertIsNone(cn.clamp_pct(None))
        self.assertIsNone(cn.clamp_pct("12"))


class WindowsFrom(unittest.TestCase):
    def test_windows_come_back_in_claude_ai_display_order(self):
        got = [(w.key, w.label, w.used) for w in cn.windows_from(claude_entry())]
        self.assertEqual(got, [
            ("session", "Current session", 12),
            ("weekly", "All models", 51),
            ("claude-weekly-scoped-fable", "Fable", 72),
        ])

    def test_scoped_windows_drop_codexbars_only_suffix(self):
        entry = {"usage": {"extraRateWindows": [
            {"title": "Fable only", "id": "x", "window": {"usedPercent": 72}}]}}
        self.assertEqual(cn.windows_from(entry)[0].label, "Fable")

    def test_an_untitled_scoped_window_is_labelled_scoped(self):
        entry = {"usage": {"extraRateWindows": [{"window": {"usedPercent": 5}}]}}
        self.assertEqual(cn.windows_from(entry)[0].label, "Scoped")

    def test_session_is_its_own_group_and_the_rest_are_weekly(self):
        self.assertEqual([w.group for w in cn.windows_from(claude_entry())],
                         ["session", "weekly", "weekly"])

    def test_a_window_without_a_percentage_is_skipped(self):
        entry = {"usage": {"primary": {"resetsAt": "2026-09-07T20:10:00Z"},
                           "secondary": {"usedPercent": 51}}}
        self.assertEqual([w.key for w in cn.windows_from(entry)], ["weekly"])

    def test_a_null_tertiary_is_skipped(self):
        entry = claude_entry()
        self.assertIsNone(entry["usage"]["tertiary"])
        self.assertNotIn("tertiary", [w.key for w in cn.windows_from(entry)])

    def test_a_tertiary_window_is_labelled_model_and_grouped_weekly(self):
        w = cn.windows_from({"usage": {"tertiary": {"usedPercent": 30}}})[0]
        self.assertEqual((w.key, w.label, w.group), ("tertiary", "Model", "weekly"))

    def test_an_errored_provider_has_no_windows(self):
        entry = codex_entry()
        self.assertTrue(entry["error"])
        self.assertEqual(cn.windows_from(entry), [])

    def test_an_entry_with_no_usage_at_all_has_no_windows(self):
        self.assertEqual(cn.windows_from({}), [])
        self.assertEqual(cn.windows_from(None), [])

    def test_reset_timestamps_are_carried_through(self):
        self.assertEqual(cn.windows_from(claude_entry())[0].resets_at,
                         "2026-09-07T20:10:00Z")

    def test_a_window_with_no_reset_time_carries_none(self):
        entry = {"usage": {"primary": {"usedPercent": 12}}}
        self.assertIsNone(cn.windows_from(entry)[0].resets_at)


if __name__ == "__main__":
    unittest.main()
