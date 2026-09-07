"""The waybar payload: what lands in the bar, and the claude.ai-shaped
breakdown that lands in the tooltip."""

import unittest
from datetime import datetime, timedelta, timezone

from support import claude_entry, codex_entry, fixture, load, strip_markup

cn = load()

BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)


def tooltip_of(data):
    return cn.render(data, NOW)["tooltip"]


class WorstSession(unittest.TestCase):
    """The bar carries the highest *session* percent across providers, the same
    number the GNOME panel indicator shows."""

    def test_the_highest_session_window_wins(self):
        data = [{"provider": "claude", "usage": {"primary": {"usedPercent": 12}}},
                {"provider": "codex", "usage": {"primary": {"usedPercent": 40}}}]
        self.assertEqual(cn.worst_session(data), 40)

    def test_a_high_weekly_window_does_not_win(self):
        # Claude's Fable cap sits at 72% while the session is at 12%; the bar
        # tracks the session, matching the GNOME panel.
        self.assertEqual(cn.worst_session(fixture("live.json")), 12)

    def test_a_provider_with_no_session_window_falls_back_to_its_first(self):
        data = [{"provider": "claude", "usage": {"secondary": {"usedPercent": 55}}}]
        self.assertEqual(cn.worst_session(data), 55)

    def test_providers_with_no_windows_are_ignored(self):
        self.assertIsNone(cn.worst_session([codex_entry()]))
        self.assertIsNone(cn.worst_session([]))


class BarText(unittest.TestCase):
    def test_the_bar_reads_label_gauge_percent(self):
        payload = cn.render(fixture("live.json"), NOW)
        self.assertTrue(payload["text"].startswith("AI "))
        self.assertTrue(payload["text"].endswith(" 12%"))
        self.assertIn(cn.gauge(12), payload["text"])

    def test_percentages_under_ten_keep_two_digits(self):
        data = [{"provider": "claude", "usage": {"primary": {"usedPercent": 4}}}]
        self.assertTrue(cn.render(data, NOW)["text"].endswith(" 04%"))

    def test_the_class_carries_the_tone(self):
        for pct, want in ((12, "ok"), (72, "warn"), (95, "critical")):
            data = [{"provider": "claude", "usage": {"primary": {"usedPercent": pct}}}]
            with self.subTest(pct=pct):
                self.assertEqual(cn.render(data, NOW)["class"], want)

    def test_the_percentage_field_is_the_bare_number(self):
        self.assertEqual(cn.render(fixture("live.json"), NOW)["percentage"], 12)


class Tooltip(unittest.TestCase):
    def test_each_provider_is_headed_by_its_glyph_and_name(self):
        lines = tooltip_of(fixture("live.json")).splitlines()
        self.assertIn("✱ Claude", strip_markup(lines[0]))

    def test_the_session_window_comes_first_with_its_reset_line(self):
        lines = [strip_markup(l) for l in tooltip_of([claude_entry()]).splitlines()]
        self.assertIn("Current session", lines[1])
        self.assertIn("12%", lines[1])
        self.assertEqual(lines[2].strip(), "Resets in 4 min  ·  22:10")

    def test_a_weekly_limits_heading_precedes_the_weekly_rows(self):
        lines = [strip_markup(l).strip() for l in tooltip_of([claude_entry()]).splitlines()]
        self.assertIn("Weekly limits", lines)
        self.assertLess(lines.index("Weekly limits"),
                        next(i for i, l in enumerate(lines) if "All models" in l))

    def test_the_heading_appears_once_however_many_weekly_windows_there_are(self):
        lines = [strip_markup(l).strip() for l in tooltip_of([claude_entry()]).splitlines()]
        self.assertEqual(lines.count("Weekly limits"), 1)

    def test_scoped_caps_are_listed_under_the_weekly_heading(self):
        text = strip_markup(tooltip_of([claude_entry()]))
        self.assertIn("Fable", text)
        self.assertIn("72%", text)

    def test_the_pace_summary_closes_the_provider_block(self):
        lines = [strip_markup(l).strip() for l in tooltip_of([claude_entry()]).splitlines()]
        self.assertEqual(lines[-1], "27% in reserve | Expected 39% used | Lasts until reset")

    def test_a_provider_with_no_pace_reading_simply_omits_it(self):
        entry = {"provider": "claude", "usage": {"primary": {"usedPercent": 12}}}
        self.assertNotIn("reserve", tooltip_of([entry]))

    def test_an_errored_provider_says_no_data_rather_than_vanishing(self):
        text = strip_markup(tooltip_of(fixture("live.json")))
        self.assertIn("◎ Codex", text)
        self.assertIn("no data", text)

    def test_the_error_message_explains_the_no_data(self):
        self.assertIn("codex app-server closed stdout",
                      strip_markup(tooltip_of([codex_entry()])))

    def test_rows_are_monospaced_so_the_gauges_line_up(self):
        self.assertIn("<tt>", tooltip_of([claude_entry()]))

    def test_an_unknown_provider_still_gets_a_name_and_a_glyph(self):
        entry = {"provider": "mistral", "usage": {"primary": {"usedPercent": 5}}}
        head = strip_markup(tooltip_of([entry]).splitlines()[0])
        self.assertIn("Mistral", head)
        self.assertTrue(head.strip())

    def test_markup_in_provider_data_is_escaped_not_rendered(self):
        entry = {"provider": "claude", "usage": {"extraRateWindows": [
            {"title": "Fable & <b>Opus</b> only", "id": "x",
             "window": {"usedPercent": 5}}]}}
        self.assertIn("Fable &amp; &lt;b&gt;Opus&lt;/b&gt;", tooltip_of([entry]))


class Offline(unittest.TestCase):
    """The server not answering is a data problem, and the bar should say so
    rather than blanking or lying."""

    def test_the_offline_bar_shows_an_empty_gauge_and_a_dash(self):
        payload = cn.offline("Connection refused")
        self.assertTrue(payload["text"].endswith(" —"))
        self.assertIn(cn.COLOURS["muted"], payload["text"])

    def test_the_offline_class_is_stale(self):
        self.assertEqual(cn.offline("Connection refused")["class"], "stale")

    def test_the_offline_tooltip_names_the_fix(self):
        tip = cn.offline("Connection refused")["tooltip"]
        self.assertIn("codexbar-serve", tip)
        self.assertIn("Connection refused", tip)

    def test_no_providers_enabled_reads_as_stale_not_as_zero_percent(self):
        payload = cn.render([], NOW)
        self.assertEqual(payload["class"], "stale")
        self.assertTrue(payload["text"].endswith(" —"))
        self.assertIn("No providers enabled", payload["tooltip"])

    def test_providers_that_all_failed_keep_their_blocks_in_the_tooltip(self):
        payload = cn.render([codex_entry()], NOW)
        self.assertEqual(payload["class"], "stale")
        self.assertIn("◎ Codex", strip_markup(payload["tooltip"]))
        self.assertIn("no data", payload["tooltip"])


class StaleFallback(unittest.TestCase):
    """One failed fetch shouldn't throw away a good reading. CodexBar itself
    falls back to its last good answer rather than blanking, and so should the
    bar: dated numbers, visibly dated, beat no numbers."""

    def good(self):
        return cn.render([claude_entry()], NOW)

    def test_the_last_good_figure_stays_in_the_bar(self):
        out = cn.stale_reading(self.good(), "timed out")
        self.assertEqual(out["text"], self.good()["text"])

    def test_but_it_is_marked_stale_so_it_greys_out(self):
        self.assertEqual(cn.stale_reading(self.good(), "timed out")["class"], "stale")

    def test_the_tooltip_says_the_reading_is_dated_and_why(self):
        tip = cn.stale_reading(self.good(), "timed out")["tooltip"]
        self.assertIn("Current session", strip_markup(tip))  # the reading is kept
        self.assertIn("timed out", strip_markup(tip))
        self.assertIn("last good reading", strip_markup(tip).lower())

    def test_a_dated_reading_keeps_its_percentage_for_waybar_states(self):
        self.assertEqual(cn.stale_reading(self.good(), "x")["percentage"], 12)

    def test_with_nothing_cached_there_is_nothing_to_fall_back_to(self):
        out = cn.stale_reading(None, "Connection refused")
        self.assertEqual(out, cn.offline("Connection refused"))

    def test_a_cached_reading_that_was_itself_stale_is_not_recycled(self):
        # Otherwise a dash from three days ago outlives the reason for it.
        self.assertEqual(cn.stale_reading(cn.offline("old"), "new"),
                         cn.offline("new"))


class AfterFetch(unittest.TestCase):
    """What actually reaches the bar, once a fetch has either worked or not."""

    def good(self):
        return cn.render([claude_entry()], NOW)

    def test_a_good_reading_goes_straight_through(self):
        good = self.good()
        self.assertEqual(cn.after_fetch(good, None), good)

    def test_a_failed_fetch_falls_back_to_the_cached_reading(self):
        out = cn.after_fetch(cn.offline("timed out"), self.good())
        self.assertEqual(out["text"], self.good()["text"])

    def test_a_failed_fetch_with_no_cache_shows_the_offline_state(self):
        self.assertEqual(cn.after_fetch(cn.offline("refused"), None),
                         cn.offline("refused"))

    def test_no_providers_enabled_is_not_a_failed_fetch(self):
        # The server answered; it just had nothing to report. Swapping in an
        # older reading would hide that, and hide it indefinitely.
        answered = cn.render([], NOW)
        self.assertEqual(cn.after_fetch(answered, self.good()), answered)

    def test_providers_that_all_failed_is_not_a_failed_fetch_either(self):
        answered = cn.render([codex_entry()], NOW)
        self.assertEqual(cn.after_fetch(answered, self.good()), answered)


if __name__ == "__main__":
    unittest.main()
