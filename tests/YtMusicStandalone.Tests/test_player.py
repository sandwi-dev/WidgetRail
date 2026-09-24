"""Muted, isolated real-WebView2 smoke test; never connects to a user's music account."""
import http.server
import io
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import time
import tempfile
import wave

buffer = io.BytesIO()
with wave.open(buffer, "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(8000)
    wav.writeframes(b"\0\0" * 8000 * 20)
data = buffer.getvalue()

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_): pass
    def do_GET(self):
        match = re.fullmatch(r"bytes=(\d+)-(\d*)", self.headers.get("Range", ""))
        start = int(match[1]) if match else 0
        end = min(int(match[2]), len(data) - 1) if match and match[2] else len(data) - 1
        self.send_response(206 if match else 200)
        self.send_header("Content-Type", "audio/wav")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        if match: self.send_header("Content-Range", f"bytes {start}-{end}/{len(data)}")
        self.end_headers()
        self.wfile.write(data[start:end+1])

server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
host = Path(sys.argv[1]).resolve()
profile = tempfile.TemporaryDirectory(prefix="WidgetRail-YtMusic-player-test-")
def cleanup_profile():
    for attempt in range(4):
        try:
            profile.cleanup()
            return
        except PermissionError:
            time.sleep(.1 * (2 ** attempt))
    profile.cleanup()
process = subprocess.Popen([str(host), "--parent-pid", str(os.getpid()), "--profile", profile.name], cwd=host.parent,
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
messages = queue.Queue()
def read():
    for line in process.stdout:
        try: messages.put(json.loads(line))
        except ValueError: pass
threading.Thread(target=read, daemon=True).start()
def send(identifier, method, args=None):
    process.stdin.write(json.dumps(dict(id=identifier, method=method, args=args or {})) + "\n")
    process.stdin.flush()
def wait(predicate):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError("Player exited before responding: " + str(process.returncode))
        try:
            value = messages.get(timeout=min(.5, max(.1, deadline - time.monotonic())))
        except queue.Empty:
            continue
        if "error" in value or value.get("event") == "failed":
            raise AssertionError("Player reported an error: " + str(value.get("error", "failed")))
        if predicate(value): return value
    raise AssertionError("Player response timed out")

try:
    send(1, "ready")
    wait(lambda m: m.get("id") == 1)
    send(2, "load", dict(url=f"http://127.0.0.1:{server.server_port}/fixture.wav", generation=1,
                        trackId="fixture", title="Muted test", artist="WidgetRail", artwork="", volume=0))
    wait(lambda m: m.get("event") == "state" and m["state"]["playing"] and m["state"]["duration"] > 0)
    send(3, "pause")
    wait(lambda m: m.get("id") == 3)
    wait(lambda m: m.get("event") == "state" and not m["state"]["playing"])
    send(4, "seek", {"value": 5})
    wait(lambda m: m.get("event") == "state" and m["state"]["position"] >= 4.9)
    send(5, "toggle")
    wait(lambda m: m.get("event") == "state" and m["state"]["playing"])
    send(6, "stop")
    wait(lambda m: m.get("id") == 6)
    process.stdin.close()
    process.wait(timeout=8)
    assert process.returncode == 0
    cleanup_profile()
    assert not Path(profile.name).exists(), "Player did not release its browser profile handles"
    print("PASS real player: initialize, muted playback, pause, seek, resume, stop, parent-pipe cleanup")
finally:
    if process.poll() is None: process.kill(); process.wait(timeout=3)
    server.shutdown()
    server.server_close()
    cleanup_profile()
