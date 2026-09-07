"""Two-column tooltip layout, and folding a shared weekly reset into its
heading. Both exist to keep the panel short: stacked, two providers with their
own reset under every window runs to sixteen lines."""

import unittest
from datetime import datetime, timedelta, timezone

from support import claude_entry, codex_entry, fixture, load, strip_markup

cn = load()

BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)

WEEK = "2026-09-08T04:00:00Z"      # Tue 06:00 Berlin
LATER = "2026-09-09T04:00:00Z"     # Wed 06:00 Berlin


def two_weeklies(a=WEEK, b=WEEK):
    return {"provider": "claude", "usage": {
        "primary": {"usedPercent": 33, "resetsAt": "2026-09-07T20:10:00Z"},
        "secondary": {"usedPercent": 54, "resetsAt": a},
        "extraRateWindows": [
            {"title": "Fable only", "id": "f", "window": {"usedPercent": 75, "resetsAt": b}}],
    }}


def plain_lines(text):
    return [strip_markup(l) for l in text.splitlines()]


class SharedWeeklyReset(unittest.TestCase):
    def test_one_shared_reset_is_named_once_in_the_heading(self):
        lines = plain_lines(cn.tooltip([two_weeklies()], NOW))
        heading = next(l for l in lines if "Weekly limits" in l)
        self.assertEqual(heading.strip(), "Weekly limits · Tue 06:00")

    def test_and_then_the_weekly_rows_carry_no_reset_of_their_own(self):
        lines = plain_lines(cn.tooltip([two_weeklies()], NOW))
        self.assertEqual(len([l for l in lines if "Resets in" in l]), 1)

    def test_weekly_windows_with_different_resets_keep_their_own_lines(self):
        lines = plain_lines(cn.tooltip([two_weeklies(WEEK, LATER)], NOW))
        heading = next(l for l in lines if "Weekly limits" in l)
        self.assertEqual(heading.strip(), "Weekly limits")
        self.assertEqual(len([l for l in lines if "Resets in" in l]), 3)

    def test_the_session_window_always_keeps_its_countdown(self):
        # The session is the one you're spending right now, so "in 4 min"
        # matters more there than the wall-clock time does.
        lines = plain_lines(cn.tooltip([two_weeklies()], NOW))
        self.assertTrue(any("Resets in 4 min" in l for l in lines))

    def test_a_lone_weekly_window_is_folded_too(self):
        entry = {"provider": "claude", "usage": {
            "secondary": {"usedPercent": 54, "resetsAt": WEEK}}}
        lines = plain_lines(cn.tooltip([entry], NOW))
        self.assertEqual(next(l for l in lines if "Weekly" in l).strip(),
                         "Weekly limits · Tue 06:00")





class Wrapping(unittest.TestCase):
    """Long text has to stay inside its column, or it runs into the next one."""

    def test_no_tooltip_line_exceeds_gtks_ceiling(self):
        for data in ([], fixture("live.json"), [claude_entry()], [codex_entry()]):
            for l in plain_lines(cn.tooltip(data, NOW)):
                with self.subTest(line=l):
                    self.assertLessEqual(len(l.rstrip()), cn.TOOLTIP_CHARS)

    def test_the_whole_error_message_survives_the_wrapping(self):
        text = " ".join(strip_markup(cn.tooltip(fixture("live.json"), NOW)).split())
        self.assertIn("codex app-server closed stdout", text)

    def test_a_long_pace_summary_survives_the_wrapping(self):
        text = " ".join(strip_markup(cn.tooltip(fixture("live.json"), NOW)).split())
        self.assertIn("Lasts until reset", text)

    def test_prose_wraps_at_the_ceiling_rather_than_wherever_gtk_would(self):
        # Long enough to need a break, so the break is ours to place.
        lines = plain_lines(cn.tooltip([codex_entry()], NOW))
        self.assertGreater(len(lines), 2)
        text = " ".join(strip_markup(cn.tooltip([codex_entry()], NOW)).split())
        self.assertIn("codex app-server closed stdout", text)



class Glyphs(unittest.TestCase):
    def test_every_provider_glyph_is_one_jetbrains_mono_actually_has(self):
        # Verified against JetBrainsMonoNerdFont-Regular.ttf: each of these is
        # present at 0.600 em, the same advance as every other glyph. A glyph
        # the font lacks falls back to another face at an unknown width, which
        # is exactly what knocks the second column out of alignment.
        verified = set("◉◎△⌘◆•")
        for pid, (name, glyph) in cn.PROVIDERS.items():
            with self.subTest(provider=pid):
                self.assertIn(glyph, verified)


if __name__ == "__main__":
    unittest.main()
