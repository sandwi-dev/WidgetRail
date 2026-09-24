"""Package-private YouTube Music catalogue and stream resolver. No desktop companion.

Stdio carries bounded JSON; the loopback server serves only previously resolved
audio under unguessable URLs. It never accepts an arbitrary destination URL.
"""
import concurrent.futures
import ctypes
from ctypes import wintypes
import http.server
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import threading
import time
from urllib.parse import urlparse

MAX_ITEMS = 500
MAX_LINE = 2 * 1024 * 1024
VIDEO_ID = re.compile(r"^[A-Za-z0-9_-]{11}$")


def browser_executable(environment=None):
    environment = os.environ if environment is None else environment
    # Match Now Playing's preference: installed Chrome first, then Edge.
    candidates = [Path(environment.get(name, "")) / suffix
                  for suffix in ("Google/Chrome/Application/chrome.exe", "Microsoft/Edge/Application/msedge.exe")
                  for name in ("PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA")]
    return next((path for path in candidates if path.is_file()), None)


def browser_arguments(profile, port):
    if not 1 <= port <= 65535:
        raise ValueError("invalid_browser_port")
    return [f"--remote-debugging-port={port}", "--remote-debugging-address=127.0.0.1",
            "--user-data-dir=" + str(profile), "--no-first-run", "--no-default-browser-check",
            "--disable-background-mode", "--new-window",
            "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fmusic.youtube.com%2F"]


def make_client(auth):
    import requests
    from ytmusicapi import YTMusic
    class BoundedSession(requests.Session):
        def request(self, method, url, **kwargs):
            kwargs.setdefault("timeout", (8, 15))
            return super().request(method, url, **kwargs)
    return YTMusic(auth=json.dumps(auth) if auth else None, requests_session=BoundedSession())


def protected(data, decrypt=False):
    """Windows DPAPI, current user; auth is never stored as plaintext JSON."""
    class Blob(ctypes.Structure):
        _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]
    buffer = ctypes.create_string_buffer(data)
    source = Blob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
    target = Blob()
    crypt = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    function = crypt.CryptUnprotectData if decrypt else crypt.CryptProtectData
    function.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                         ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
    function.restype = wintypes.BOOL
    kernel.LocalFree.argtypes = [ctypes.c_void_p]
    kernel.LocalFree.restype = ctypes.c_void_p
    if not function(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(target)):
        raise OSError("Session protection failed")
    try:
        return ctypes.string_at(target.data, target.size)
    finally:
        kernel.LocalFree(target.data)


def normalize(item, kind=None):
    if not isinstance(item, dict):
        return None
    video = str(item.get("videoId") or "")
    key = video or str(item.get("playlistId") or item.get("browseId") or "")
    if not key or len(key) > 256:
        return None
    inferred = str(item.get("resultType") or item.get("type") or "").lower()
    if not inferred:
        inferred = "album" if key.startswith("MPRE") else "artist" if key.startswith("UC") else "playlist"
    kind = "song" if VIDEO_ID.fullmatch(video) else kind or inferred
    if kind not in {"song", "album", "artist", "playlist"}:
        return None
    title = str(item.get("title") or item.get("artist") or "Untitled")[:160]
    artists = item.get("artists") or []
    subtitle = ", ".join(str(a.get("name", "")) for a in artists if isinstance(a, dict))[:180]
    thumbnails = [t for t in item.get("thumbnails", []) if isinstance(t, dict) and str(t.get("url", "")).startswith("https://")]
    image = min(thumbnails, key=lambda t: abs(int(t.get("width") or 300) - 300), default={})
    return dict(id=key, kind=kind, title=title, subtitle=subtitle, artwork=str(image.get("url", ""))[:2048])


def items(values, kind=None):
    return [entry for value in (values or [])[:MAX_ITEMS] if (entry := normalize(value, kind))]


def make_downloader(auth):
    from yt_dlp import YoutubeDL
    import http.cookiejar

    class Quiet:
        def debug(self, *_): pass
        def warning(self, *_): pass
        def error(self, *_): pass

    downloader = YoutubeDL({"quiet": True, "no_warnings": True, "logger": Quiet(),
                           "format": "bestaudio[ext=m4a]/bestaudio/best", "noplaylist": True,
                           "socket_timeout": 12, "retries": 1, "extractor_retries": 1,
                           "cachedir": False, "skip_download": True,
                           "js_runtimes": {"quickjs": {"path": str(Path(sys.executable).parent / "qjs.exe")}}})
    for part in auth.get("cookie", "").split(";"):
        name, separator, value = part.strip().partition("=")
        if separator:
            downloader.cookiejar.set_cookie(http.cookiejar.Cookie(0, name, value, None, False,
                ".youtube.com", True, True, "/", True, True, None, True, None, None, {}))
    return downloader


class ResolverLane:
    """One reusable mutable extractor, never entered concurrently.

    Contending foreground requests use request-local instances, bounded by the
    existing service worker pool, rather than queueing behind an older selection.
    """
    MAX_EXTRACTIONS = 32

    def __init__(self):
        self.lock = threading.Lock()
        self.downloader = None
        self.generation = None
        self.uses = 0

    @staticmethod
    def dispose(downloader):
        if downloader is not None:
            try: downloader.close()
            except Exception: pass

    def clear(self):
        downloader, self.downloader = self.downloader, None
        self.generation, self.uses = None, 0
        self.dispose(downloader)

    def retire(self, generation=None):
        # An in-flight extractor owns its resources until its finally block.
        if self.lock.acquire(blocking=False):
            try:
                if generation is None or generation != self.generation:
                    self.clear()
            finally:
                self.lock.release()

    def extract(self, video, auth, generation, is_current):
        if not is_current(generation):
            raise RuntimeError("session_changed")
        url = "https://music.youtube.com/watch?v=" + video
        if not self.lock.acquire(blocking=False):
            temporary = make_downloader(auth)
            try:
                if not is_current(generation):
                    raise RuntimeError("session_changed")
                return temporary.extract_info(url, download=False)
            finally:
                self.dispose(temporary)
        try:
            if not is_current(generation):
                raise RuntimeError("session_changed")
            if self.generation != generation or self.uses >= self.MAX_EXTRACTIONS:
                self.clear()
            if self.downloader is None:
                self.downloader = make_downloader(auth)
                self.generation = generation
            if not is_current(generation):
                raise RuntimeError("session_changed")
            self.uses += 1
            return self.downloader.extract_info(url, download=False)
        except Exception:
            # Do not retain partially failed extractor state for the next song.
            self.clear()
            raise
        finally:
            if not is_current(generation):
                self.clear()
            self.lock.release()


class MusicService:
    def __init__(self, root):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self.auth_path = self.root / "session.dpapi"
        self.auth = {}
        if self.auth_path.exists():
            try:
                self.auth = json.loads(protected(self.auth_path.read_bytes(), decrypt=True))
            except Exception:
                pass  # An expired/different-user session returns to sign-in.
        self.lock = threading.RLock()
        self.auth_gate = threading.Lock()
        self.auth_cancel = threading.Event()
        self.generation = 0
        self.cache = {}
        self.streams = {}
        self.stream_keys = {}
        self.resolving = {}
        self.prefetch_work = None
        self.foreground_resolver = ResolverLane()
        self.prefetch_resolver = ResolverLane()
        self.stopping = threading.Event()
        self.prefetch = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        self.secret = secrets.token_urlsafe(32)
        self.server = self.create_server()
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def is_current_session(self, generation):
        with self.lock:
            return generation == self.generation and not self.stopping.is_set()

    def retire_resolvers(self, generation=None):
        self.foreground_resolver.retire(generation)
        self.prefetch_resolver.retire(generation)

    def client(self):
        with self.lock:
            auth = dict(self.auth)
        return make_client(auth)

    def status(self, _):
        with self.lock:
            return {"connected": bool(self.auth)}

    def disconnect(self, _):
        self.auth_cancel.set()
        self.stopping.set()  # Cancels any login currently awaiting user input.
        with self.auth_gate:
            with self.lock:
                self.auth = {}
                self.generation += 1
                self.cache.clear()
                self.streams.clear()
                self.stream_keys.clear()
                self.auth_path.unlink(missing_ok=True)
                generation = self.generation
            self.retire_resolvers(generation)
        self.stopping.clear()
        return self.status({})

    def cancel_signin(self, _):
        self.auth_cancel.set()
        return {}

    def signin(self, _):
        # A dedicated temporary profile never inspects the user's existing browser session.
        import tempfile
        import shutil
        import urllib.request
        import websocket
        from ytmusicapi.helpers import get_authorization
        with self.auth_gate:
            self.auth_cancel.clear()
            browser = browser_executable()
            if browser is None:
                raise RuntimeError("browser_unavailable")
            profile = tempfile.mkdtemp(prefix="ytmusic-login-", dir=self.root)
            process = None
            connection = None
            try:
                with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                    probe.bind(("127.0.0.1", 0))
                    port = probe.getsockname()[1]
                process = subprocess.Popen([str(browser), *browser_arguments(profile, port)],
                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                deadline = time.monotonic() + 300
                while time.monotonic() < deadline and not self.stopping.is_set() and not self.auth_cancel.wait(.5):
                    if connection is None:
                        if process.poll() is not None:
                            raise RuntimeError("signin_window_closed")
                        try:
                            with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=3) as response:
                                endpoint = json.load(response)["webSocketDebuggerUrl"]
                        except OSError:
                            continue
                        endpoint_uri = urlparse(endpoint)
                        if endpoint_uri.scheme != "ws" or endpoint_uri.hostname != "127.0.0.1" or endpoint_uri.port != port:
                            raise RuntimeError("invalid_browser_endpoint")
                        connection = websocket.create_connection(endpoint, timeout=3, suppress_origin=True)
                    connection.send(json.dumps({"id": 1, "method": "Storage.getCookies"}))
                    while True:
                        message = json.loads(connection.recv())
                        if message.get("id") == 1:
                            break
                    cookies = {c["name"]: c["value"] for c in message.get("result", {}).get("cookies", [])
                               if c.get("domain", "").lstrip(".") in {"youtube.com", "music.youtube.com"}}
                    sapisid = cookies.get("__Secure-3PAPISID") or cookies.get("SAPISID")
                    if not sapisid:
                        continue
                    headers = {"cookie": "; ".join(k + "=" + v for k, v in cookies.items()),
                               "origin": "https://music.youtube.com", "x-goog-authuser": "0",
                               "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36",
                               "authorization": get_authorization(sapisid + " https://music.youtube.com")}
                    # Prove the captured session can read a private collection before saving it.
                    try:
                        make_client(headers).get_library_playlists(limit=1)
                    except Exception:
                        continue
                    if self.stopping.is_set() or self.auth_cancel.is_set():
                        raise RuntimeError("signin_cancelled")
                    blob = protected(json.dumps(headers).encode("utf-8"))
                    temporary = self.auth_path.with_suffix(".tmp")
                    temporary.write_bytes(blob)
                    temporary.replace(self.auth_path)
                    with self.lock:
                        self.auth = headers
                        self.generation += 1
                        self.cache.clear()
                        self.streams.clear()
                        self.stream_keys.clear()
                        generation = self.generation
                    self.retire_resolvers(generation)
                    return {"connected": True}
                raise RuntimeError("signin_timeout")
            finally:
                if connection:
                    try:
                        connection.send(json.dumps({"id": 2, "method": "Browser.close"}))
                        connection.close()
                    except Exception:
                        pass
                if process:
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.terminate()
                        try:
                            process.wait(timeout=3)
                        except subprocess.TimeoutExpired:
                            pass
                # Only the newly created, package-owned profile is eligible for deletion.
                shutil.rmtree(profile, ignore_errors=True)

    def browse(self, args):
        kind, value = str(args.get("kind", "")), str(args.get("value", ""))
        if len(value) > 256:
            raise ValueError("invalid_query")
        key = kind + ":" + value
        with self.lock:
            generation = self.generation
            cached = self.cache.get(key)
            if kind != "radio" and cached and cached[0] > time.monotonic():
                return cached[1]
        client = self.client()
        if kind == "home":
            shelves = client.get_home(limit=6)
            # Stable ordering: promote this shelf while preserving provider order
            # among all other recommendations (and among any matching shelves).
            shelves = sorted(shelves, key=lambda shelf: str(shelf.get("title") or "").strip().casefold() != "mixed for you")
            entries = []
            for shelf in shelves:
                for entry in items(shelf.get("contents", [])):
                    entry["section"] = str(shelf.get("title") or "Recommendations")[:160]
                    entries.append(entry)
            result = {"title": "Home", "items": entries[:MAX_ITEMS]}
        elif kind == "search":
            if not value.strip():
                return {"title": "Search", "items": []}
            result = {"title": "Search results", "items": items(client.search(value[:96], limit=60))}
        elif kind == "library":
            readers = {"playlists": (client.get_library_playlists, "playlist"), "songs": (client.get_library_songs, "song"),
                       "albums": (client.get_library_albums, "album"), "artists": (client.get_library_artists, "artist")}
            reader, item_kind = readers[value]
            # The playlist endpoint has no order parameter. Other library
            # endpoints support a provider-side order before applying the limit.
            options = {"limit": MAX_ITEMS}
            if value != "playlists":
                options["order"] = "recently_added"
            result = {"title": "Your " + value, "items": items(reader(**options), item_kind)}
        elif kind == "playlist":
            page = client.get_playlist(value, limit=MAX_ITEMS)
            result = {"title": str(page.get("title", "Playlist"))[:160], "items": items(page.get("tracks"))}
        elif kind == "album":
            page = client.get_album(value)
            result = {"title": str(page.get("title", "Album"))[:160], "items": items(page.get("tracks"))}
        elif kind == "artist":
            page = client.get_artist(value)
            contents = [v for section in ("songs", "albums", "singles") for v in (page.get(section) or {}).get("results", [])]
            result = {"title": str(page.get("name", "Artist"))[:160], "items": items(contents)}
        elif kind == "radio" and VIDEO_ID.fullmatch(value):
            page = client.get_watch_playlist(videoId=value, radio=True, limit=100)
            result = {"title": "Song radio", "items": items(page.get("tracks"))}
        else:
            raise ValueError("invalid_page")
        with self.lock:
            if generation == self.generation and kind != "radio":
                if len(self.cache) >= 24:
                    self.cache.pop(next(iter(self.cache)))
                self.cache[key] = (time.monotonic() + 300, result)
        return result

    def resolve(self, args, *, speculative=False):
        video = str(args.get("videoId", ""))
        if not VIDEO_ID.fullmatch(video):
            raise ValueError("invalid_video")
        with self.lock:
            key = self.stream_keys.get(video)
            if key and self.streams.get(key, {}).get("expires", 0) > time.time() + 120:
                return {"url": self.local_url(key)}
            auth = dict(self.auth)
            generation = self.generation
            identity = (generation, video)
            pending = self.resolving.get(identity)
            owner = pending is None
            if owner:
                pending = concurrent.futures.Future()
                self.resolving[identity] = pending
        if not owner:
            # A foreground selection joins its in-flight prefetch instead of
            # repeating YouTube requests and the JavaScript signature solver.
            return pending.result(timeout=40)
        try:
            result = self.resolve_uncached(video, auth, generation, speculative=speculative)
            pending.set_result(result)
            return result
        except Exception as error:
            pending.set_exception(error)
            raise
        finally:
            with self.lock:
                if self.resolving.get(identity) is pending:
                    del self.resolving[identity]

    def resolve_uncached(self, video, auth, generation, *, speculative=False):
        lane = self.prefetch_resolver if speculative else self.foreground_resolver
        info = lane.extract(video, auth, generation, self.is_current_session)
        url = str(info.get("url", ""))
        parsed = urlparse(url)
        if parsed.scheme != "https" or not (parsed.hostname or "").endswith(".googlevideo.com"):
            raise ValueError("unsupported_stream")
        key = secrets.token_urlsafe(24)
        headers = {k: v for k, v in info.get("http_headers", {}).items() if k.lower() in {"user-agent", "referer", "origin"}}
        with self.lock:
            if generation != self.generation or self.stopping.is_set():
                raise RuntimeError("session_changed")
            if len(self.streams) >= 32:
                self.streams.pop(next(iter(self.streams)))
            self.streams[key] = {"url": url, "headers": headers, "expires": time.time() + 900}
            self.stream_keys[video] = key
            if len(self.stream_keys) > 64:
                self.stream_keys.pop(next(iter(self.stream_keys)))
        return {"url": self.local_url(key)}

    def local_url(self, key):
        return f"http://127.0.0.1:{self.server.server_port}/{self.secret}/{key}"

    def create_server(self):
        service = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *_): pass
            def do_GET(self):
                import requests
                parts = self.path.split("/")
                if len(parts) != 3 or not secrets.compare_digest(parts[1], service.secret):
                    self.send_error(404)
                    return
                with service.lock:
                    stream = service.streams.get(parts[2])
                if not stream:
                    self.send_error(404)
                    return
                headers = dict(stream["headers"])
                requested_range = self.headers.get("Range", "")
                if requested_range and re.fullmatch(r"bytes=\d*-\d*", requested_range):
                    headers["Range"] = requested_range
                try:
                    with requests.get(stream["url"], headers=headers, stream=True, timeout=(8, 15), allow_redirects=False) as response:
                        if response.status_code not in (200, 206):
                            self.send_error(502)
                            return
                        self.send_response(response.status_code)
                        for name in ("Content-Type", "Content-Length", "Content-Range", "Accept-Ranges"):
                            if name in response.headers:
                                self.send_header(name, response.headers[name])
                        self.send_header("Access-Control-Allow-Origin", "https://ytmusic.widgetrail.internal")
                        self.send_header("Cache-Control", "no-store")
                        self.end_headers()
                        for chunk in response.iter_content(65536):
                            if service.stopping.is_set(): break
                            self.wfile.write(chunk)
                except (OSError, requests.RequestException):
                    self.close_connection = True
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        server.daemon_threads = True
        return server

    def handle(self, method, args):
        if method == "prefetch":
            # One speculative track cannot occupy foreground request workers.
            with self.lock:
                if self.prefetch_work is None or self.prefetch_work.done():
                    self.prefetch_work = self.prefetch.submit(self.safe_prefetch, dict(args))
            return {}
        if method not in {"status", "signin", "cancel_signin", "disconnect", "browse", "resolve"}:
            raise ValueError("unknown_method")
        return getattr(self, method)(args)

    def safe_prefetch(self, args):
        try: self.resolve(args, speculative=True)
        except Exception: pass

    def close(self):
        self.auth_cancel.set()
        self.stopping.set()
        self.retire_resolvers()
        self.server.shutdown()
        self.server.server_close()
        self.prefetch.shutdown(wait=False, cancel_futures=True)


def main():
    # Python's isolated mode does not add the script's directory to sys.path.
    root = Path(os.environ["LOCALAPPDATA"]) / "WidgetRail" / "applications" / "widgetrail.samples.ytmusic"
    service = MusicService(root)
    gate = threading.Lock()
    capacity = threading.BoundedSemaphore(12)
    output = sys.stdout
    sys.stdout = sys.stderr  # Third-party prints must never enter the protocol stream.
    def reply(value):
        encoded = json.dumps(value, ensure_ascii=True, separators=(",", ":"))
        if len(encoded) > MAX_LINE:
            encoded = json.dumps({"id": value.get("id"), "error": "response_too_large"})
        with gate:
            output.write(encoded + "\n")
            output.flush()
    def execute(request):
        try:
            reply({"id": request["id"], "result": service.handle(request["method"], request.get("args") or {})})
        except Exception:
            reply({"id": request.get("id"), "error": "request_failed"})
        finally:
            capacity.release()
    pool = concurrent.futures.ThreadPoolExecutor(max_workers=4)
    try:
        while True:
            line = sys.stdin.readline(MAX_LINE + 1)
            if not line: break
            if len(line) > MAX_LINE: break
            request = json.loads(line)
            if not isinstance(request, dict) or not isinstance(request.get("id"), int): break
            if capacity.acquire(blocking=False): pool.submit(execute, request)
            else: reply({"id": request["id"], "error": "busy"})
    finally:
        service.close()
        pool.shutdown(wait=False, cancel_futures=True)


if __name__ == "__main__":
    main()
