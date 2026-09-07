"""`--print` renders the same breakdown for a terminal, which is what clicking
the module shows. Pango markup is the tooltip's language, not a terminal's."""

import unittest
from datetime import datetime, timedelta, timezone

from support import claude_entry, load

cn = load()

BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)


class PlainText(unittest.TestCase):
    def payload(self):
        return cn.render([claude_entry()], NOW)

    def test_no_markup_survives_into_the_terminal(self):
        out = cn.plain_text(self.payload())
        self.assertNotIn("<", out)
        self.assertNotIn(">", out)

    def test_escaped_characters_come_back_as_themselves(self):
        entry = {"provider": "claude", "usage": {"extraRateWindows": [
            {"title": "Fable & Opus only", "id": "x", "window": {"usedPercent": 5}}]}}
        self.assertIn("Fable & Opus", cn.plain_text(cn.render([entry], NOW)))

    def test_the_headline_is_the_same_figure_the_bar_shows(self):
        self.assertTrue(cn.plain_text(self.payload()).startswith("AI 12%"))

    def test_the_gauges_and_the_breakdown_are_kept(self):
        out = cn.plain_text(self.payload())
        self.assertIn("Current session", out)
        self.assertIn("█░░░░░░░░░", out)
        self.assertIn("Weekly limits", out)
        self.assertIn("Resets in 4 min", out)

    def test_an_offline_reading_explains_itself_in_the_terminal_too(self):
        out = cn.plain_text(cn.offline("Connection refused"))
        self.assertIn("Codenotch offline", out)
        self.assertIn("Connection refused", out)
        self.assertIn("codexbar-serve", out)


if __name__ == "__main__":
    unittest.main()
