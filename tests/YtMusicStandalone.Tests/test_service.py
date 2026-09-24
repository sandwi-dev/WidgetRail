"""Offline tests; run with the packaged runtime so dependency imports match distribution."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request

source = Path(__file__).resolve().parents[2] / "samples/YtMusicWidget/Service/service.py"
spec = importlib.util.spec_from_file_location("music_service", source)
music = importlib.util.module_from_spec(spec)
spec.loader.exec_module(music)


class ServiceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.service = music.MusicService(self.directory.name)

    def tearDown(self):
        self.service.close()
        self.directory.cleanup()

    def test_dpapi_round_trip_and_tamper_rejection(self):
        secret = b'{"cookie":"synthetic-session-for-test"}'
        protected = music.protected(secret)
        self.assertNotIn(secret, protected)
        self.assertEqual(secret, music.protected(protected, decrypt=True))
        with self.assertRaises(OSError):
            music.protected(protected[:-4], decrypt=True)

    def test_disconnect_removes_only_owned_session(self):
        other = Path(self.directory.name) / "unrelated.txt"
        other.write_text("keep")
        self.service.auth_path.write_bytes(music.protected(b'{"cookie":"test"}'))
        self.service.auth = {"cookie": "test"}
        self.service.disconnect({})
        self.assertFalse(self.service.auth_path.exists())
        self.assertEqual("keep", other.read_text())

    def test_audio_proxy_rejects_arbitrary_urls_and_wrong_tokens(self):
        for path in ("/https://example.org", "/wrong/key", "/" + self.service.secret + "/missing"):
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(f"http://127.0.0.1:{self.service.server.server_port}" + path)
            self.assertEqual(404, error.exception.code)

    def test_song_radio_calls_watch_playlist_and_never_uses_cached_radio(self):
        class Fake:
            calls = 0
            def get_watch_playlist(self, **kwargs):
                self.calls += 1
                assert kwargs == dict(videoId="M7lc1UVf-VE", radio=True, limit=100)
                return {"tracks": [{"videoId": "M7lc1UVf-VE", "title": "Seed"}]}
        fake = Fake()
        self.service.client = lambda: fake
        for _ in range(2):
            result = self.service.browse({"kind": "radio", "value": "M7lc1UVf-VE"})
            self.assertEqual("Seed", result["items"][0]["title"])
        self.assertEqual(2, fake.calls)

    def test_catalogue_cache_is_bounded_and_disconnect_invalidates_it(self):
        class Fake:
            calls = 0
            def search(self, query, **_):
                self.calls += 1
                return [{"videoId": "M7lc1UVf-VE", "title": query}]
        fake = Fake()
        self.service.client = lambda: fake
        for _ in range(2): self.service.browse({"kind": "search", "value": "same"})
        self.assertEqual(1, fake.calls)
        for i in range(30): self.service.browse({"kind": "search", "value": str(i)})
        self.assertLessEqual(len(self.service.cache), 24)
        self.service.disconnect({})
        self.assertEqual({}, self.service.cache)

    def test_library_requests_recently_added_only_where_supported(self):
        class Fake:
            def get_library_playlists(self, limit):
                assert limit == music.MAX_ITEMS
                return [{"playlistId": "PLtest", "title": "Playlist"}]
            def get_library_songs(self, limit, order):
                assert limit == music.MAX_ITEMS and order == "recently_added"
                return [{"videoId": "M7lc1UVf-VE", "title": "Song"}]
            def get_library_albums(self, limit, order):
                assert limit == music.MAX_ITEMS and order == "recently_added"
                return [{"browseId": "MPREtest", "title": "Album"}]
            def get_library_artists(self, limit, order):
                assert limit == music.MAX_ITEMS and order == "recently_added"
                return [{"browseId": "UCtest", "title": "Artist"}]
        self.service.client = lambda: Fake()
        for name in ("playlists", "songs", "albums", "artists"):
            self.assertEqual(1, len(self.service.browse({"kind": "library", "value": name})["items"]))

    def test_home_preserves_provider_sections_and_order(self):
        class Fake:
            def get_home(self, limit):
                assert limit == 6
                return [{"title": "Quick picks", "contents": [{"videoId": "M7lc1UVf-VE", "title": "First"}]},
                        {"title": "For you", "contents": [{"playlistId": "PLtest", "title": "Mix"}]}]
        self.service.client = lambda: Fake()
        result = self.service.browse({"kind": "home", "value": ""})
        self.assertEqual(["Quick picks", "For you"], [item["section"] for item in result["items"]])
        self.assertEqual(["First", "Mix"], [item["title"] for item in result["items"]])

    def test_home_promotes_mixed_for_you_without_reordering_other_shelves(self):
        class Fake:
            def get_home(self, limit):
                return [{"title": title, "contents": [{"playlistId": "PL" + str(index), "title": title}]}
                        for index, title in enumerate(["Quick picks", "Rediscover", "Mixed for you", "New releases"])]
        self.service.client = lambda: Fake()
        result = self.service.browse({"kind": "home", "value": ""})
        self.assertEqual(["Mixed for you", "Quick picks", "Rediscover", "New releases"],
                         [item["section"] for item in result["items"]])

    def test_normalization_bounds_content_and_rejects_non_https_art(self):
        item = music.normalize({"videoId": "M7lc1UVf-VE", "title": "x" * 1000,
            "thumbnails": [{"url": "http://localhost/private"}]})
        self.assertEqual(160, len(item["title"]))
        self.assertEqual("", item["artwork"])
        self.assertEqual("song", item["kind"])
        self.assertIsNone(music.normalize({"title": "no identity"}))

    def test_selection_joins_inflight_prefetch(self):
        import concurrent.futures
        import threading
        entered, release, joined = threading.Event(), threading.Event(), threading.Event()
        calls = []
        def resolve_uncached(video, auth, generation):
            calls.append(video)
            entered.set()
            if not release.wait(3):
                raise TimeoutError("test did not release extraction")
            return {"url": "http://127.0.0.1/fixture"}
        self.service.resolve_uncached = resolve_uncached
        self.service.handle("prefetch", {"videoId": "M7lc1UVf-VE"})
        self.assertTrue(entered.wait(3))
        pending = next(iter(self.service.resolving.values()))
        original_result = pending.result
        def join(timeout=None):
            joined.set()
            return original_result(timeout)
        try:
            with patch.object(pending, "result", side_effect=join):
                with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                    selection = executor.submit(self.service.resolve, {"videoId": "M7lc1UVf-VE"})
                    try:
                        self.assertTrue(joined.wait(3), "Selection did not join the pending extraction")
                        self.assertFalse(selection.done())
                    finally:
                        release.set()
                    self.assertEqual({"url": "http://127.0.0.1/fixture"}, selection.result(timeout=3))
            self.assertEqual(["M7lc1UVf-VE"], calls)
            self.assertEqual({}, self.service.resolving)
        finally:
            release.set()

    def test_failed_extraction_can_be_retried(self):
        with patch.object(self.service, "resolve_uncached", side_effect=[RuntimeError("fixture failure"), {"url": "http://127.0.0.1/fixture"}]) as resolve:
            with self.assertRaises(RuntimeError): self.service.resolve({"videoId": "M7lc1UVf-VE"})
            self.assertEqual({}, self.service.resolving)
            self.assertEqual({"url": "http://127.0.0.1/fixture"}, self.service.resolve({"videoId": "M7lc1UVf-VE"}))
            self.assertEqual(2, resolve.call_count)

    def test_prefetch_has_no_unbounded_backlog(self):
        import threading
        entered, release = threading.Event(), threading.Event()
        calls = []
        def resolve(args):
            calls.append(args["videoId"])
            entered.set()
            release.wait(3)
        self.service.resolve = resolve
        try:
            self.service.handle("prefetch", {"videoId": "M7lc1UVf-VE"})
            self.assertTrue(entered.wait(3))
            for i in range(50): self.service.handle("prefetch", {"videoId": "other" + str(i)})
        finally:
            release.set()
            self.service.prefetch.shutdown(wait=True)
        self.assertEqual(["M7lc1UVf-VE"], calls)

    def test_invalid_stream_id_never_reaches_extractor(self):
        with patch("yt_dlp.YoutubeDL") as downloader:
            with self.assertRaises(ValueError): self.service.resolve({"videoId": "https://localhost/"})
            downloader.assert_not_called()

    def test_browser_prefers_chrome_and_falls_back_to_edge(self):
        root = Path(self.directory.name)
        chrome = root / "programs/Google/Chrome/Application/chrome.exe"
        edge = root / "programs-x86/Microsoft/Edge/Application/msedge.exe"
        for path in (chrome, edge):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
        environment = {"PROGRAMFILES": str(root / "programs"), "PROGRAMFILES(X86)": str(root / "programs-x86"), "LOCALAPPDATA": str(root / "local")}
        self.assertEqual(chrome, music.browser_executable(environment))
        chrome.unlink()
        self.assertEqual(edge, music.browser_executable(environment))

    def test_login_uses_explicit_loopback_debugging_port(self):
        arguments = music.browser_arguments("profile", 49123)
        self.assertIn("--remote-debugging-port=49123", arguments)
        self.assertIn("--remote-debugging-address=127.0.0.1", arguments)
        self.assertNotIn("--enable-automation", arguments)
        self.assertNotIn("--remote-allow-origins=*", arguments)
        with self.assertRaises(ValueError): music.browser_arguments("profile", 0)

    def test_cancel_login_does_not_stop_audio_service(self):
        self.service.handle("cancel_signin", {})
        self.assertTrue(self.service.auth_cancel.is_set())
        self.assertFalse(self.service.stopping.is_set())


if __name__ == "__main__":
    unittest.main()
