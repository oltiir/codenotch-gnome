"""Two colour jobs on one module: which provider this is, and how close to the
limit it is. They can't share an element -- Anthropic's coral sits at 1.07:1
against the 90% red, so a coral module at 5% and a red one at 95% would look
the same. So the label carries identity and the gauge and figure carry
severity."""

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
    def test_claude_wears_anthropics_coral(self):
        self.assertIn(cn.LABEL_COLOURS["claude"], bar("claude", 39))

    def test_codex_keeps_the_default_hue(self):
        self.assertIn(cn.COLOURS["ok"], bar("codex", 3))
        self.assertNotIn(cn.LABEL_COLOURS["claude"], bar("codex", 3))

    def test_the_identity_colour_does_not_change_with_usage(self):
        for used in (5, 75, 95):
            with self.subTest(used=used):
                self.assertIn(cn.LABEL_COLOURS["claude"], bar("claude", used))

    def test_the_combined_module_has_no_providers_identity(self):
        text = cn.render(fixture("live.json"), NOW)["text"]
        self.assertNotIn(cn.LABEL_COLOURS["claude"], text)


class Severity(unittest.TestCase):
    def test_the_figure_turns_with_the_threshold_even_for_claude(self):
        # The whole point: Claude being coral must not cost it the warning.
        self.assertIn(cn.COLOURS["warn"], bar("claude", 75))
        self.assertIn(cn.COLOURS["critical"], bar("claude", 95))

    def test_an_untroubled_claude_shows_no_warning_colours(self):
        text = bar("claude", 39)
        self.assertNotIn(cn.COLOURS["warn"], text)
        self.assertNotIn(cn.COLOURS["critical"], text)

    def test_a_module_with_no_reading_is_muted_all_through(self):
        out = cn.render([], NOW, "claude")
        self.assertIn(cn.COLOURS["muted"], out["text"])
        self.assertNotIn(cn.LABEL_COLOURS["claude"], out["text"])


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
        colours = dict(cn.COLOURS, **cn.LABEL_COLOURS)
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

    def test_the_identity_colour_could_not_have_served_as_a_severity_one(self):
        # This is why identity and severity live on different elements: coral
        # is hue-adjacent to both the 70% and the 90% colour, so a coral
        # module would have been unreadable as a warning.
        coral = cn.LABEL_COLOURS["claude"]
        self.assertLess(self.hue_apart(coral, cn.COLOURS["warn"]), 40)
        self.assertLess(self.hue_apart(coral, cn.COLOURS["critical"]), 40)


if __name__ == "__main__":
    unittest.main()
