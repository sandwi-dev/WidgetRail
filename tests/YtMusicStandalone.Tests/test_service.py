"""Offline tests; run with the packaged runtime so dependency imports match distribution."""
import importlib.util
import json
import time
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


class FakeDownloader:
    def __init__(self, auth, on_extract=None):
        self.auth = dict(auth)
        self.on_extract = on_extract
        self.calls = 0
        self.active = False
        self.closed = False
        self.url = "https://fixture.googlevideo.com/audio"

    def extract_info(self, url, download=False):
        if self.active or self.closed:
            raise AssertionError("Extractor reused concurrently or after disposal")
        self.active = True
        self.calls += 1
        try:
            if self.on_extract:
                self.on_extract(self, url)
            return {"url": self.url, "http_headers": {}}
        finally:
            self.active = False

    def close(self):
        if self.active:
            raise AssertionError("Disposed an in-flight extractor")
        self.closed = True


class StreamCacheTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)

    def tearDown(self):
        self.directory.cleanup()

    def entry(self, expiry=None, extra=""):
        expiry = int(time.time() + 3600) if expiry is None else expiry
        return {"url": f"https://fixture.googlevideo.com/audio?expire={expiry}&signature={extra}",
                "headers": {"User-Agent": "fixture"}, "expires": expiry}

    def test_encrypted_restart_cache_contains_no_local_proxy_handles(self):
        cache = music.StreamUrlCache(self.root, {"cookie": "account-a"})
        entry = self.entry()
        cache.put("AAAAAAAAAAA", dict(entry, video="AAAAAAAAAAA", generation=4, localToken="do-not-persist"))
        encrypted = cache.path.read_bytes()
        self.assertNotIn(entry["url"].encode(), encrypted)
        self.assertNotIn(b"account-a", encrypted)
        document = json.loads(music.protected(encrypted, decrypt=True))
        self.assertEqual({"url", "headers", "expires"}, set(document["entries"]["AAAAAAAAAAA"]))
        self.assertEqual(entry, music.StreamUrlCache(self.root, {"cookie": "account-a"}).get("AAAAAAAAAAA"))
        self.assertIsNone(music.StreamUrlCache(self.root, {"cookie": "account-b"}).get("AAAAAAAAAAA"))

    def test_expiry_margin_and_unknown_expiry_are_cache_misses(self):
        now = int(time.time())
        cache = music.StreamUrlCache(self.root, {})
        for value in (self.entry(now + 60), {"url": "https://fixture.googlevideo.com/audio", "headers": {}, "expires": now + 3600}):
            cache.put("AAAAAAAAAAA", value)
            self.assertIsNone(cache.get("AAAAAAAAAAA"))
        cache.put("AAAAAAAAAAA", self.entry(now + 3600))
        with patch.object(music.time, "time", return_value=now + 3481):
            self.assertIsNone(music.StreamUrlCache(self.root, {}).get("AAAAAAAAAAA"))

    def test_corrupt_and_oversized_cache_fall_back_to_empty(self):
        path = self.root / "stream-urls.dpapi"
        path.write_bytes(b"not a DPAPI blob")
        self.assertEqual({}, music.StreamUrlCache(self.root, {}).entries)
        path.write_bytes(b"x" * (music.StreamUrlCache.MAX_BYTES + 1))
        self.assertEqual({}, music.StreamUrlCache(self.root, {}).entries)

    def test_untrusted_url_headers_and_extended_expiry_are_rejected(self):
        cache = music.StreamUrlCache(self.root, {})
        entry = self.entry()
        invalid = [dict(entry, url="https://localhost/audio?expire=" + str(entry["expires"])),
                   dict(entry, headers={"Cookie": "not-allowed"}),
                   dict(entry, headers={"User-Agent": "bad\r\nheader"}),
                   dict(entry, expires=entry["expires"] + 600)]
        for value in invalid:
            cache.put("AAAAAAAAAAA", value)
            self.assertIsNone(cache.get("AAAAAAAAAAA"))

    def test_entry_count_and_byte_budget_evict_oldest_entries(self):
        self.assertEqual(512, music.StreamUrlCache.MAX_ENTRIES)
        cache = music.StreamUrlCache(self.root, {})
        with patch.object(music.StreamUrlCache, "MAX_ENTRIES", 3):
            for index in range(3): cache.put(f"{index:011d}", self.entry())
            cache.get("00000000000")
            cache.put("00000000003", self.entry())
            self.assertIsNone(cache.get("00000000001"))
            self.assertIsNotNone(cache.get("00000000000"))
        with patch.object(music.StreamUrlCache, "MAX_BYTES", 80 * 1024):
            for index in range(20): cache.put(f"{index:011d}", self.entry(extra="x" * 4000))
            self.assertLess(len(cache.entries), 20)
            self.assertLessEqual(cache.path.stat().st_size, music.StreamUrlCache.MAX_BYTES)

    def test_write_failure_does_not_fail_playback_and_reset_preserves_other_data(self):
        cache = music.StreamUrlCache(self.root, {})
        other = self.root / "session.dpapi"
        other.write_bytes(b"unrelated-auth-fixture")
        with patch.object(music.os, "replace", side_effect=PermissionError("fixture")):
            cache.put("AAAAAAAAAAA", self.entry())
        self.assertIsNotNone(cache.get("AAAAAAAAAAA"))
        self.assertEqual([], list(self.root.glob(".stream-cache-*.tmp")))
        cache.put("AAAAAAAAAAA", self.entry())
        self.assertTrue(cache.path.exists())
        cache.reset({"cookie": "new-account"})
        self.assertFalse(cache.path.exists())
        self.assertEqual(b"unrelated-auth-fixture", other.read_bytes())


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
                return {"tracks": [
                    {"videoId": "M7lc1UVf-VE", "title": "Seed", "thumbnail": [
                        {"url": "https://example.invalid/seed-small.jpg", "width": 60},
                        {"url": "https://example.invalid/seed.jpg", "width": 300}]},
                    {"videoId": "AAAAAAAAAAA", "title": "Next", "thumbnail": [
                        {"url": "https://example.invalid/next.jpg", "width": 300}]}]}
        fake = Fake()
        self.service.client = lambda: fake
        for _ in range(2):
            result = self.service.browse({"kind": "radio", "value": "M7lc1UVf-VE"})
            self.assertEqual("Seed", result["items"][0]["title"])
            self.assertEqual(["https://example.invalid/seed.jpg", "https://example.invalid/next.jpg"],
                             [item["artwork"] for item in result["items"]])
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

    def test_library_album_uses_browse_id_and_tracks_inherit_album_artwork(self):
        class Fake:
            def get_library_playlists(self, **_): return []
            def get_library_songs(self, **_): return []
            def get_library_artists(self, **_): return []
            def get_library_albums(self, **_):
                return [{"browseId": "MPRE_album", "playlistId": "OLAK_playback", "title": "Album"}]
            def get_album(self, browse_id):
                assert browse_id == "MPRE_album"
                return {"title": "Album", "thumbnails": [{"url": "https://example.invalid/cover.jpg", "width": 300}],
                        "tracks": [
                            {"videoId": "M7lc1UVf-VE", "title": "First", "thumbnails": None},
                            {"videoId": "AAAAAAAAAAA", "title": "Second"},
                            {"videoId": "BBBBBBBBBBB", "title": "Third", "thumbnails": [
                                {"url": "https://example.invalid/track.jpg", "width": 300}]}]}
        self.service.client = lambda: Fake()
        album = self.service.browse({"kind": "library", "value": "albums"})["items"][0]
        result = self.service.browse({"kind": album["kind"], "value": album["id"]})
        self.assertEqual(["First", "Second", "Third"], [track["title"] for track in result["items"]])
        self.assertEqual(["https://example.invalid/cover.jpg", "https://example.invalid/cover.jpg",
                          "https://example.invalid/track.jpg"], [track["artwork"] for track in result["items"]])
        self.assertTrue(all(track["kind"] == "song" for track in result["items"]))

    def test_album_identity_and_cover_fallback_do_not_change_playlists_or_admit_unsafe_art(self):
        album = {"browseId": "MPRE_album", "playlistId": "OLAK_playback", "type": "Album"}
        self.assertEqual("MPRE_album", music.normalize(album)["id"])
        playlist = {"browseId": "VLplaylist", "playlistId": "PLplaylist", "type": "Playlist"}
        self.assertEqual("PLplaylist", music.normalize(playlist)["id"])
        class Fake:
            def get_album(self, _):
                return {"thumbnails": [{"url": "http://localhost/private"}],
                        "tracks": [{"videoId": "M7lc1UVf-VE", "title": "Song", "thumbnails": []}]}
        self.service.client = lambda: Fake()
        result = self.service.browse({"kind": "album", "value": "MPRE_album"})
        self.assertEqual("", result["items"][0]["artwork"])

    def test_large_playlist_is_bounded_in_provider_order(self):
        class Fake:
            def get_playlist(self, playlist_id, limit):
                assert playlist_id == "PLtest" and limit == 500
                # A provider page may cross the requested limit.
                return {"title": "Large playlist", "tracks": [
                    {"videoId": "M7lc1UVf-VE", "title": str(index)} for index in range(750)]}
        self.service.client = lambda: Fake()
        result = self.service.browse({"kind": "playlist", "value": "PLtest"})
        self.assertEqual([str(index) for index in range(500)], [item["title"] for item in result["items"]])

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

    def test_normalization_accepts_catalogue_and_radio_artwork_without_relaxing_url_policy(self):
        for field in ("thumbnails", "thumbnail"):
            with self.subTest(field=field):
                item = {"videoId": "M7lc1UVf-VE", field: [
                    {"url": "http://localhost/private", "width": 300},
                    {"url": "https://example.invalid/art.jpg", "width": 320}]}
                self.assertEqual("https://example.invalid/art.jpg", music.normalize(item)["artwork"])
                item[field] = [{"url": "file:///private", "width": 300}, {"url": "http://localhost/private"}]
                self.assertEqual("", music.normalize(item)["artwork"])
                item[field] = None
                self.assertEqual("", music.normalize(item)["artwork"])
        self.assertEqual("https://example.invalid/radio.jpg", music.normalize({
            "videoId": "M7lc1UVf-VE", "thumbnails": [],
            "thumbnail": [{"url": "https://example.invalid/radio.jpg", "width": 300}]})["artwork"])

    def test_selection_joins_inflight_prefetch(self):
        import concurrent.futures
        import threading
        entered, release, joined = threading.Event(), threading.Event(), threading.Event()
        calls = []
        def resolve_uncached(video, auth, generation, **_):
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
        def resolve(args, **_):
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

    def test_resolver_reuses_separate_foreground_and_prefetch_instances(self):
        instances = []
        def create(auth):
            instance = FakeDownloader(auth)
            instances.append(instance)
            return instance
        with patch.object(music, "make_downloader", side_effect=create):
            for video in ("AAAAAAAAAAA", "BBBBBBBBBBB"):
                self.service.resolve({"videoId": video})
            for video in ("CCCCCCCCCCC", "DDDDDDDDDDD"):
                self.service.resolve({"videoId": video}, speculative=True)
            self.service.resolve({"videoId": "EEEEEEEEEEE"})
        self.assertEqual(2, len(instances))
        self.assertEqual([3, 2], [instance.calls for instance in instances])
        self.assertFalse(any(instance.closed for instance in instances))
        self.service.close()
        self.assertTrue(all(instance.closed for instance in instances))

    def test_busy_foreground_does_not_delay_a_new_selection(self):
        import concurrent.futures
        import threading
        entered, release = threading.Event(), threading.Event()
        instances = []
        def extracting(instance, url):
            if url.endswith("AAAAAAAAAAA"):
                entered.set()
                if not release.wait(3): raise TimeoutError("Fixture not released")
        def create(auth):
            instance = FakeDownloader(auth, extracting)
            instances.append(instance)
            return instance
        with patch.object(music, "make_downloader", side_effect=create):
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
                old = executor.submit(self.service.resolve, {"videoId": "AAAAAAAAAAA"})
                try:
                    self.assertTrue(entered.wait(3))
                    new = executor.submit(self.service.resolve, {"videoId": "BBBBBBBBBBB"})
                    new.result(timeout=2)
                    self.assertFalse(old.done(), "Fixture did not hold the old extraction")
                    self.assertEqual(2, len(instances))
                    self.assertTrue(instances[1].closed, "Temporary contender was retained")
                    self.assertFalse(instances[0].closed, "Busy extractor was disposed")
                finally:
                    release.set()
                old.result(timeout=2)
            self.service.resolve({"videoId": "CCCCCCCCCCC"})
        self.assertEqual(2, len(instances))
        self.assertEqual(2, instances[0].calls)

    def test_disconnect_retires_both_idle_instances_and_old_streams(self):
        instances = []
        def create(auth):
            instance = FakeDownloader(auth)
            instances.append(instance)
            return instance
        self.service.auth = {"cookie": "fixture=old-account"}
        with patch.object(music, "make_downloader", side_effect=create):
            self.service.resolve({"videoId": "AAAAAAAAAAA"})
            self.service.resolve({"videoId": "BBBBBBBBBBB"}, speculative=True)
            self.service.disconnect({})
            self.assertTrue(all(instance.closed for instance in instances))
            self.assertEqual({}, self.service.streams)
            self.service.resolve({"videoId": "AAAAAAAAAAA"})
        self.assertEqual(3, len(instances))
        self.assertEqual({}, instances[2].auth)

    def test_account_change_during_extraction_discards_old_result_safely(self):
        import concurrent.futures
        import threading
        entered, release = threading.Event(), threading.Event()
        instances = []
        def extracting(instance, url):
            if url.endswith("AAAAAAAAAAA"):
                entered.set()
                if not release.wait(3): raise TimeoutError("Fixture not released")
        def create(auth):
            instance = FakeDownloader(auth, extracting)
            instance.url += "?expire=" + str(int(time.time() + 3600))
            instances.append(instance)
            return instance
        self.service.auth = {"cookie": "fixture=old-account"}
        with patch.object(music, "make_downloader", side_effect=create):
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
                old = executor.submit(self.service.resolve, {"videoId": "AAAAAAAAAAA"})
                try:
                    self.assertTrue(entered.wait(3))
                    executor.submit(self.service.disconnect, {}).result(timeout=2)
                    self.assertFalse(instances[0].closed)
                    executor.submit(self.service.resolve, {"videoId": "BBBBBBBBBBB"}).result(timeout=2)
                    self.assertEqual({}, instances[1].auth)
                finally:
                    release.set()
                with self.assertRaisesRegex(RuntimeError, "session_changed"): old.result(timeout=2)
        self.assertTrue(instances[0].closed)
        self.assertNotIn("AAAAAAAAAAA", self.service.stream_keys)
        self.assertEqual({}, self.service.resolving)
        self.assertIsNone(self.service.url_cache.get("AAAAAAAAAAA"), "Old-session resolution repopulated the disk cache")

    def test_reusable_extractor_is_recycled_after_failure_or_use_limit(self):
        instances = []
        def create(auth):
            instance = FakeDownloader(auth)
            instances.append(instance)
            if len(instances) == 1:
                instance.on_extract = lambda *_: (_ for _ in ()).throw(RuntimeError("fixture failure"))
            return instance
        with patch.object(music, "make_downloader", side_effect=create):
            with self.assertRaises(RuntimeError): self.service.resolve({"videoId": "AAAAAAAAAAA"})
            self.assertTrue(instances[0].closed)
            for index in range(music.ResolverLane.MAX_EXTRACTIONS + 1):
                self.service.resolve({"videoId": f"{index:011d}"})
        self.assertEqual(3, len(instances))
        self.assertEqual(music.ResolverLane.MAX_EXTRACTIONS, instances[1].calls)
        self.assertTrue(instances[1].closed)
        self.assertEqual(1, instances[2].calls)

    def test_restart_uses_cached_url_and_mints_a_new_proxy_handle(self):
        instances = []
        def create(auth):
            instance = FakeDownloader(auth)
            instance.url += "?expire=" + str(int(time.time() + 3600))
            instances.append(instance)
            return instance
        with patch.object(music, "make_downloader", side_effect=create):
            first = self.service.resolve({"videoId": "AAAAAAAAAAA"})
            self.service.close()
            self.service = music.MusicService(self.directory.name)
            second = self.service.resolve({"videoId": "AAAAAAAAAAA"})
        self.assertEqual(1, len(instances), "Restart unnecessarily resolved the same signed URL")
        self.assertNotEqual(first["url"], second["url"], "Restart reused the old local capability")

    def test_revoked_cached_url_refreshes_once_and_keeps_range_requests(self):
        instances = []
        def create(auth):
            instance = FakeDownloader(auth)
            identity = len(instances)
            instance.on_extract = lambda extractor, _: setattr(extractor, "url",
                f"https://fixture.googlevideo.com/audio?expire={int(time.time() + 3600)}&signature={identity}-{extractor.calls}")
            instances.append(instance)
            return instance
        class Response:
            def __init__(self, status):
                self.status_code = status
                self.headers = {"Content-Length": "5", "Content-Type": "audio/mp4", "Content-Range": "bytes 0-4/5"}
            def __enter__(self): return self
            def __exit__(self, *_): pass
            def iter_content(self, _): yield b"audio"
        with patch.object(music, "make_downloader", side_effect=create):
            self.service.resolve({"videoId": "AAAAAAAAAAA"})
            self.service.close()
            self.service = music.MusicService(self.directory.name)
            local = self.service.resolve({"videoId": "AAAAAAAAAAA"})["url"]
            with patch("requests.get", side_effect=[Response(403), Response(206)]) as upstream:
                request = urllib.request.Request(local, headers={"Range": "bytes=0-4"})
                with urllib.request.urlopen(request, timeout=5) as response:
                    self.assertEqual(206, response.status)
                    self.assertEqual(b"audio", response.read())
                self.assertEqual(2, upstream.call_count)
                self.assertNotEqual(upstream.call_args_list[0].args[0], upstream.call_args_list[1].args[0])
                self.assertTrue(all(call.kwargs["headers"]["Range"] == "bytes=0-4" and not call.kwargs["allow_redirects"]
                                    for call in upstream.call_args_list))
            self.assertEqual(2, len(instances))
            with patch("requests.get", side_effect=[Response(403), Response(403)]) as upstream:
                with self.assertRaises(urllib.error.HTTPError) as error:
                    urllib.request.urlopen(local, timeout=5)
                self.assertEqual(502, error.exception.code)
                self.assertEqual(2, upstream.call_count, "Rejected URLs caused an unbounded retry")
            self.assertIsNone(self.service.url_cache.get("AAAAAAAAAAA"))

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
