"""Colour thresholds, the block gauge and the reset line. Times are passed in
explicitly so the assertions hold in any timezone."""

import unittest
from datetime import datetime, timedelta, timezone

from support import load

cn = load()

# Europe/Berlin in September, so the fixture's 20:10Z reads as 22:10 local.
BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)


class Tone(unittest.TestCase):
    """Green under 70, yellow from 70, red from 90 -- percent used."""

    def test_thresholds_turn_exactly_at_seventy_and_ninety(self):
        self.assertEqual(
            [cn.tone(p) for p in (0, 69, 70, 89, 90, 100)],
            ["ok", "ok", "warn", "warn", "critical", "critical"],
        )


class GaugeFill(unittest.TestCase):
    """How many of the ten cells are filled."""

    def test_a_full_gauge_means_the_quota_is_gone(self):
        self.assertEqual(cn.filled_cells(100), 10)

    def test_ninety_five_percent_is_not_a_full_gauge(self):
        # Rounding alone would fill all ten here, which would read as
        # exhausted while 5% of the quota is still there.
        self.assertEqual(cn.filled_cells(95), 9)
        self.assertEqual(cn.filled_cells(99), 9)

    def test_cells_round_to_the_nearest_tenth(self):
        self.assertEqual(cn.filled_cells(68), 7)
        self.assertEqual(cn.filled_cells(64), 6)
        self.assertEqual(cn.filled_cells(5), 1)

    def test_barely_used_and_unused_both_show_an_empty_gauge(self):
        self.assertEqual(cn.filled_cells(4), 0)
        self.assertEqual(cn.filled_cells(0), 0)


class Gauge(unittest.TestCase):
    """One pango span holding all ten cells, the way the CPU/MEM modules do it."""

    def test_the_gauge_is_ten_cells_wide_at_any_percentage(self):
        for pct in (0, 1, 50, 99, 100):
            with self.subTest(pct=pct):
                cells = cn.gauge(pct).split(">")[1].split("<")[0]
                self.assertEqual(len(cells), 10)

    def test_filled_cells_come_first_then_empty_ones(self):
        self.assertIn("███████░░░", cn.gauge(68))

    def test_the_gauge_carries_the_tone_colour(self):
        self.assertIn(cn.COLOURS["ok"], cn.gauge(12))
        self.assertIn(cn.COLOURS["warn"], cn.gauge(72))
        self.assertIn(cn.COLOURS["critical"], cn.gauge(95))

    def test_an_empty_gauge_is_muted_rather_than_coloured(self):
        self.assertIn(cn.COLOURS["muted"], cn.gauge(0))
        self.assertNotIn(cn.COLOURS["ok"], cn.gauge(0))


class Countdown(unittest.TestCase):
    """claude.ai's phrasing: "59 min", "13 hr 49 min", "2 days 3 hr"."""

    def at(self, **kw):
        return cn.countdown((NOW + timedelta(**kw)).isoformat(), NOW)

    def test_under_an_hour_is_minutes(self):
        self.assertEqual(self.at(minutes=59), "59 min")

    def test_under_a_day_is_hours_and_minutes(self):
        self.assertEqual(self.at(hours=13, minutes=49), "13 hr 49 min")

    def test_a_whole_number_of_hours_drops_the_minutes(self):
        self.assertEqual(self.at(hours=2), "2 hr")

    def test_over_a_day_is_days_and_hours(self):
        self.assertEqual(self.at(days=2, hours=3), "2 days 3 hr")

    def test_one_day_is_singular(self):
        self.assertEqual(self.at(days=1, minutes=1), "1 day")

    def test_a_whole_number_of_days_drops_the_hours(self):
        self.assertEqual(self.at(days=3), "3 days")

    def test_a_window_already_past_reads_now(self):
        self.assertEqual(self.at(minutes=-5), "now")
        self.assertEqual(self.at(seconds=0), "now")

    def test_a_missing_or_unparsable_timestamp_has_no_countdown(self):
        self.assertEqual(cn.countdown(None, NOW), "")
        self.assertEqual(cn.countdown("not a date", NOW), "")


class ClockText(unittest.TestCase):
    def test_a_reset_later_today_shows_just_the_time(self):
        # 20:10Z is 22:10 in Berlin, the same day as NOW.
        self.assertEqual(cn.clock_text("2026-09-07T20:10:00Z", NOW), "22:10")

    def test_a_reset_on_another_day_names_the_day(self):
        # 04:00Z Sep 8 is 06:00 Berlin, the Tuesday after NOW.
        self.assertEqual(cn.clock_text("2026-09-08T04:00:00Z", NOW), "Tue 06:00")

    def test_a_missing_timestamp_has_no_clock(self):
        self.assertEqual(cn.clock_text(None, NOW), "")


class ResetLine(unittest.TestCase):
    def test_the_countdown_and_the_wall_clock_sit_side_by_side(self):
        self.assertEqual(cn.reset_line("2026-09-07T20:10:00Z", NOW),
                         "Resets in 4 min  ·  22:10")

    def test_a_window_at_its_reset_reads_resetting(self):
        self.assertEqual(cn.reset_line(NOW.isoformat(), NOW), "Resetting")

    def test_a_window_with_no_reset_time_has_no_line(self):
        self.assertEqual(cn.reset_line(None, NOW), "")


if __name__ == "__main__":
    unittest.main()
