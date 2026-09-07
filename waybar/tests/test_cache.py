"""The last reading is kept on disk so a freshly started bar paints the numbers
at once instead of sitting empty through a ~10s fetch -- and so a second
provider's module reads it instead of fetching again."""

import json
import os
import tempfile
import unittest

from support import load

cn = load()


class CachePath(unittest.TestCase):
    def test_the_cache_lives_in_the_runtime_dir_when_there_is_one(self):
        with tempfile.TemporaryDirectory() as d:
            os.environ["XDG_RUNTIME_DIR"] = d
            try:
                self.assertTrue(cn.cache_path().startswith(d))
            finally:
                del os.environ["XDG_RUNTIME_DIR"]

    def test_it_falls_back_to_a_temp_dir_without_one(self):
        os.environ.pop("XDG_RUNTIME_DIR", None)
        self.assertTrue(cn.cache_path().endswith(".json"))


class CacheRoundTrip(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        os.environ["XDG_RUNTIME_DIR"] = self.dir.name
        self.addCleanup(lambda: os.environ.pop("XDG_RUNTIME_DIR", None))

    def test_a_saved_reading_comes_back(self):
        cn.save_cache(cn.cache_entry([{"provider": "claude"}], now=99.0))
        got = cn.load_cache()
        self.assertEqual(got["usage"], [{"provider": "claude"}])
        self.assertEqual(got["at"], 99.0)

    def test_nothing_saved_yet_reads_as_nothing_cached(self):
        self.assertIsNone(cn.load_cache())

    def test_a_corrupt_cache_is_ignored_rather_than_fatal(self):
        with open(cn.cache_path(), "w") as f:
            f.write("{ truncated")
        self.assertIsNone(cn.load_cache())

    def test_a_cache_that_is_not_a_reading_is_ignored(self):
        with open(cn.cache_path(), "w") as f:
            json.dump(["not", "a", "payload"], f)
        self.assertIsNone(cn.load_cache())

    def test_saving_over_an_unwritable_cache_does_not_crash_the_bar(self):
        os.environ["XDG_RUNTIME_DIR"] = "/proc/nonexistent-for-codenotch"
        cn.save_cache(cn.cache_entry([], now=1.0))  # must not raise


if __name__ == "__main__":
    unittest.main()
