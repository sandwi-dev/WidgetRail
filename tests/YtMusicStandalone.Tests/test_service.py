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

    def test_normalization_bounds_content_and_rejects_non_https_art(self):
        item = music.normalize({"videoId": "M7lc1UVf-VE", "title": "x" * 1000,
            "thumbnails": [{"url": "http://localhost/private"}]})
        self.assertEqual(160, len(item["title"]))
        self.assertEqual("", item["artwork"])
        self.assertEqual("song", item["kind"])
        self.assertIsNone(music.normalize({"title": "no identity"}))

    def test_invalid_stream_id_never_reaches_extractor(self):
        with patch("yt_dlp.YoutubeDL") as downloader:
            with self.assertRaises(ValueError): self.service.resolve({"videoId": "https://localhost/"})
            downloader.assert_not_called()


if __name__ == "__main__":
    unittest.main()
