"""One module per provider: `--provider claude` renders Claude alone, with its
own figure in the bar and only its own block in the tooltip."""

import unittest
from datetime import datetime, timedelta, timezone

from support import claude_entry, codex_entry, fixture, load, strip_markup

cn = load()

BERLIN = timezone(timedelta(hours=2))
NOW = datetime(2026, 9, 7, 20, 6, 5, tzinfo=timezone.utc).astimezone(BERLIN)


class Labels(unittest.TestCase):
    """Short all-caps labels, in the idiom of the stock MEM/CPU/VOL modules."""

    def test_the_known_providers_have_short_labels(self):
        self.assertEqual(cn.label_for("claude"), "CC")
        self.assertEqual(cn.label_for("codex"), "CDX")

    def test_an_unknown_provider_gets_a_label_anyway(self):
        self.assertEqual(cn.label_for("mistral"), "MIS")

    def test_no_provider_at_all_is_the_combined_reading(self):
        self.assertEqual(cn.label_for(None), "AI")


class OneProvidersBar(unittest.TestCase):
    def claude(self):
        return cn.render(fixture("live.json"), NOW, "claude")

    def test_the_bar_carries_that_providers_own_label_and_figure(self):
        out = self.claude()
        self.assertEqual(strip_markup(out["text"]), "CC █░░░░░░░░░ 12%")
        self.assertEqual(out["percentage"], 12)

    def test_the_figure_is_that_providers_session_not_the_worst_of_all(self):
        # Codex is the errored one here; a worst-of reading would hide that by
        # reporting Claude's number under both labels.
        codex = cn.render(fixture("live.json"), NOW, "codex")
        self.assertEqual(codex["class"], "stale")
        self.assertNotIn("12%", strip_markup(codex["text"]))

    def test_the_class_is_that_providers_own_tone(self):
        data = [{"provider": "claude", "usage": {"primary": {"usedPercent": 95}}},
                {"provider": "codex", "usage": {"primary": {"usedPercent": 5}}}]
        self.assertEqual(cn.render(data, NOW, "claude")["class"], "critical")
        self.assertEqual(cn.render(data, NOW, "codex")["class"], "ok")

    def test_the_tooltip_holds_only_that_provider(self):
        tip = strip_markup(self.claude()["tooltip"])
        self.assertIn("Claude", tip)
        self.assertNotIn("Codex", tip)

    def test_a_provider_the_server_never_mentioned_reads_as_stale(self):
        out = cn.render([claude_entry()], NOW, "codex")
        self.assertEqual(out["class"], "stale")
        self.assertEqual(strip_markup(out["text"]), "CDX ░░░░░░░░░░ —")
        self.assertIn("not enabled", strip_markup(out["tooltip"]))

    def test_an_errored_provider_says_why_in_its_own_tooltip(self):
        out = cn.render(fixture("live.json"), NOW, "codex")
        text = " ".join(strip_markup(out["tooltip"]).split())
        self.assertIn("codex app-server closed stdout", text)

    def test_asking_for_no_provider_still_gives_the_combined_reading(self):
        out = cn.render(fixture("live.json"), NOW)
        self.assertTrue(strip_markup(out["text"]).startswith("AI "))
        self.assertIn("Claude", strip_markup(out["tooltip"]))
        self.assertIn("Codex", strip_markup(out["tooltip"]))


class SharedFetch(unittest.TestCase):
    """Two modules, one server. The cache holds the raw reading so whichever
    module wakes first pays for the fetch and the other reads its answer --
    otherwise two modules would double the upstream load for the same data."""

    def test_a_fresh_cache_is_reused_rather_than_refetched(self):
        self.assertFalse(cn.needs_fetch({"at": 1000.0, "usage": []}, now=1000.0 + cn.REFRESH - 1))

    def test_a_stale_cache_is_refetched(self):
        self.assertTrue(cn.needs_fetch({"at": 1000.0, "usage": []}, now=1000.0 + cn.REFRESH + 1))

    def test_no_cache_at_all_means_fetch(self):
        self.assertTrue(cn.needs_fetch(None, now=1000.0))

    def test_a_cache_without_a_timestamp_means_fetch(self):
        self.assertTrue(cn.needs_fetch({"usage": []}, now=1000.0))

    def test_the_cache_carries_the_raw_reading_not_a_rendered_payload(self):
        # Rendered payloads can't be re-used by the other provider's module,
        # and they go stale differently -- the countdowns are baked in.
        entry = cn.cache_entry(fixture("live.json"), now=1234.0)
        self.assertEqual(entry["at"], 1234.0)
        self.assertEqual(entry["usage"], fixture("live.json"))


if __name__ == "__main__":
    unittest.main()


class FailedFetch(unittest.TestCase):
    """A failed fetch keeps the last good reading rather than blanking -- the
    same last-good behaviour CodexBar has upstream. A dash is reserved for
    having nothing at all."""

    def setUp(self):
        self.calls = []
        self._fetch, self._load, self._save = cn.fetch, cn.load_cache, cn.save_cache
        cn.save_cache = lambda e: self.calls.append(("save", e))
        self.addCleanup(self.restore)

    def restore(self):
        cn.fetch, cn.load_cache, cn.save_cache = self._fetch, self._load, self._save

    def stub(self, fetch_result, cached):
        cn.fetch = lambda: (self.calls.append(("fetch",)), fetch_result)[1]
        cn.load_cache = lambda: cached

    def test_a_fresh_cache_is_used_without_going_upstream(self):
        self.stub((None, "should not be called"),
                  {"at": 1000.0, "usage": fixture("live.json")})
        out = cn.reading("claude", NOW, 1000.0 + 1)
        self.assertNotIn(("fetch",), self.calls)
        self.assertTrue(strip_markup(out["text"]).endswith(" 12%"))

    def test_a_good_fetch_is_saved_for_the_other_module_to_read(self):
        self.stub((fixture("live.json"), None), None)
        cn.reading("claude", NOW, 5000.0)
        self.assertIn(("fetch",), self.calls)
        saved = [c for c in self.calls if c[0] == "save"]
        self.assertEqual(saved[0][1]["usage"], fixture("live.json"))

    def test_a_failed_fetch_keeps_the_cached_figure_but_greys_it(self):
        self.stub((None, "timed out"),
                  {"at": 0.0, "usage": fixture("live.json")})
        out = cn.reading("claude", NOW, 9999.0)
        self.assertTrue(strip_markup(out["text"]).endswith(" 12%"))
        self.assertEqual(out["class"], "stale")
        self.assertIn("timed out", strip_markup(out["tooltip"]))

    def test_a_failed_fetch_with_nothing_cached_shows_the_dash(self):
        self.stub((None, "Connection refused"), None)
        out = cn.reading("codex", NOW, 9999.0)
        self.assertEqual(strip_markup(out["text"]), "CDX ░░░░░░░░░░ —")
        self.assertEqual(out["class"], "stale")
        self.assertIn("Connection refused", strip_markup(out["tooltip"]))
