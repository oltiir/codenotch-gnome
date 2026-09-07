"""`--print` renders the same breakdown for a terminal, which is what clicking
the module shows. Pango markup is the tooltip's language, not a terminal's."""

import unittest
from datetime import datetime, timedelta, timezone

from support import claude_entry, fixture, load

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


class Headline(unittest.TestCase):
    def test_it_carries_the_same_label_the_module_shows(self):
        for pid, label in (("claude", "CC"), ("codex", "CDX")):
            with self.subTest(provider=pid):
                out = cn.render(fixture("live.json"), NOW, pid)
                self.assertTrue(cn.plain_text(out).startswith(label + " "))

    def test_the_combined_reading_is_still_headed_AI(self):
        out = cn.render(fixture("live.json"), NOW)
        self.assertTrue(cn.plain_text(out).startswith("AI "))

    def test_a_module_with_no_reading_is_headed_with_a_dash(self):
        out = cn.render([], NOW, "codex")
        self.assertTrue(cn.plain_text(out).startswith("CDX —"))


class OneColumnInTheTerminal(unittest.TestCase):
    """The tooltip lays two providers side by side to stay short. A terminal
    doesn't mind height, so `--print` stacks them and abbreviates nothing."""

    def payload(self):
        return cn.render(fixture("live.json"), NOW)

    def test_providers_stack_rather_than_sitting_side_by_side(self):
        for line in cn.plain_text(self.payload()).splitlines():
            with self.subTest(line=line):
                self.assertLessEqual(line.count("█") + line.count("░"), cn.CELLS)

    def test_nothing_is_wrapped_to_fit_a_column(self):
        out = cn.plain_text(self.payload())
        self.assertIn("27% in reserve | Expected 39% used | Lasts until reset", out)
        self.assertIn("no data · Codex returned invalid data: "
                      "codex app-server closed stdout", out)


if __name__ == "__main__":
    unittest.main()
