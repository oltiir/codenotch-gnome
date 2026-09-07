"""A module is its provider's colour while all is well, and turns gold then red
as the quota fills. The label keeps the provider's colour throughout, so two
modules are still told apart at a glance when one of them is warning."""

import unittest
from datetime import datetime, timedelta, timezone

from support import fixture, load

cn = load()

BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)


def bar(pid, used):
    data = [{"provider": pid, "usage": {"primary": {"usedPercent": used}}}]
    return cn.render(data, NOW, pid)["text"]


class Identity(unittest.TestCase):
    def test_an_untroubled_claude_is_coral_throughout(self):
        # Label, gauge and figure: the whole module, which is the state it is
        # in nearly all the time.
        text = bar("claude", 41)
        self.assertEqual(text.count(cn.PROVIDER_COLOURS["claude"]), 3)
        self.assertNotIn(cn.COLOURS["ok"], text)

    def test_codex_keeps_the_default_hue(self):
        self.assertIn(cn.COLOURS["ok"], bar("codex", 41))
        self.assertNotIn(cn.PROVIDER_COLOURS["claude"], bar("codex", 41))

    def test_the_label_keeps_the_providers_colour_even_when_warning(self):
        # So a warning module is still identifiable as Claude's.
        for used in (5, 75, 95):
            with self.subTest(used=used):
                self.assertIn(cn.PROVIDER_COLOURS["claude"], bar("claude", used))

    def test_the_combined_module_has_no_providers_identity(self):
        text = cn.render(fixture("live.json"), NOW)["text"]
        self.assertNotIn(cn.PROVIDER_COLOURS["claude"], text)


class Severity(unittest.TestCase):
    """Being coral must not cost Claude the warning, so the thresholds still
    override it on the gauge and the figure."""

    def test_the_gauge_and_figure_turn_gold_at_seventy(self):
        text = bar("claude", 75)
        self.assertEqual(text.count(cn.COLOURS["warn"]), 2)

    def test_and_red_at_ninety(self):
        text = bar("claude", 95)
        self.assertEqual(text.count(cn.COLOURS["critical"]), 2)

    def test_an_untroubled_claude_shows_no_warning_colours(self):
        text = bar("claude", 41)
        self.assertNotIn(cn.COLOURS["warn"], text)
        self.assertNotIn(cn.COLOURS["critical"], text)

    def test_a_module_with_no_reading_is_muted_all_through(self):
        out = cn.render([], NOW, "claude")
        self.assertIn(cn.COLOURS["muted"], out["text"])
        self.assertNotIn(cn.PROVIDER_COLOURS["claude"], out["text"])


class StillReadable(unittest.TestCase):
    """Guards the reason the split exists, so nobody merges it back."""

    @staticmethod
    def contrast(a, b):
        def lum(h):
            c = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
            c = [x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4
                 for x in c]
            return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
        la, lb = lum(a), lum(b)
        return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)

    BAR_BACKGROUND = "#191724"  # rose-pine @base

    def test_every_colour_is_legible_on_the_bar(self):
        colours = dict(cn.COLOURS, **cn.PROVIDER_COLOURS)
        for name, hexcode in colours.items():
            if name == "muted":
                continue  # deliberately dim: it means "no reading"
            with self.subTest(colour=name):
                self.assertGreaterEqual(
                    self.contrast(hexcode, self.BAR_BACKGROUND), 4.5)

    @staticmethod
    def hue_apart(a, b):
        """Degrees between two hues. Luminance contrast is the wrong measure
        here -- pine and love sit at 1.25:1 and are plainly blue and pink."""
        import colorsys

        def hue(h):
            r, g, bl = (int(h[i:i + 2], 16) / 255 for i in (1, 3, 5))
            return colorsys.rgb_to_hsv(r, g, bl)[0] * 360
        d = abs(hue(a) - hue(b)) % 360
        return min(d, 360 - d)

    def test_the_three_severity_colours_are_distinct_hues(self):
        for a, b in (("ok", "warn"), ("warn", "critical"), ("ok", "critical")):
            with self.subTest(pair=(a, b)):
                self.assertGreater(
                    self.hue_apart(cn.COLOURS[a], cn.COLOURS[b]), 40)

    def test_a_provider_colour_is_told_from_the_warning_by_lightness(self):
        # Coral is hue-adjacent to gold (24 degrees) and to red (14), so for a
        # provider wearing it the threshold reads as a lightness jump rather
        # than a hue change. Gold gives that; red gives much less of one, so
        # for Claude at 90% the fill level and the figure do most of the work.
        coral = cn.PROVIDER_COLOURS["claude"]
        self.assertGreater(self.contrast(coral, cn.COLOURS["warn"]), 1.8)
        self.assertLess(self.contrast(coral, cn.COLOURS["critical"]), 1.2)


if __name__ == "__main__":
    unittest.main()
