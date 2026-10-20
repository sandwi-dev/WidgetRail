"""Measure captured viewport movement, independently of UI-thread callbacks.

Requires numpy and Pillow. Use --libraries for an isolated dependency directory.
The correlation is a diagnostic estimate, not a GPU present-time measurement.
"""
import argparse
import datetime as dt
import json
import pathlib
import sys

parser = argparse.ArgumentParser()
parser.add_argument("run", type=pathlib.Path)
parser.add_argument("--libraries", type=pathlib.Path)
args = parser.parse_args()
if args.libraries:
    sys.path.insert(0, str(args.libraries.resolve()))
import numpy as np
from PIL import Image

root = args.run.resolve()
bundle = root / "native-scroll.frames"
manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8-sig"))
if manifest["status"] != "complete" or manifest["frames"].get("truncated"):
    raise ValueError("Motion analysis requires a complete, untruncated native capture.")
ready = json.loads((root / "comparison-ready.json").read_text(encoding="utf-8-sig"))
entries = [json.loads(line) for line in (bundle / "frames.ndjson").read_text().splitlines()]
record_start = dt.datetime.fromisoformat(manifest["startedUtc"]).timestamp()
movement_start = (root / "capture-started").stat().st_mtime - record_start
view = ready["viewport"]
# Window capture and viewport transforms share window coordinates. Page.ActualWidth
# can differ from the window due to layout constraints; it is not a capture scale.
# Older captures used the fixed 1360-DIP fixture window.
factor = manifest["frames"]["width"] / ready.get("windowWidth", 1360)
box = tuple(round(value * factor) for value in (
    view["X"] + 16, view["Y"] + 24,
    view["Right"] - 16, view["Bottom"] - 24))
if not (0 <= box[0] < box[2] <= manifest["frames"]["width"] and
        0 <= box[1] < box[3] <= manifest["frames"]["height"]):
    raise ValueError("Viewport coordinates do not fit the native capture.")
cache_name, cache_pixels = None, None
previous = None
rows = []
for entry in entries:
    seconds = entry["elapsedMs"] / 1000
    if not movement_start + .5 <= seconds <= movement_start + 7.5:
        continue
    if entry["file"] != cache_name:
        with Image.open(bundle / entry["file"]) as image:
            crop = image.crop(box).convert("L")
            crop = crop.resize((crop.width // 2, crop.height // 2), Image.Resampling.BILINEAR)
            cache_pixels = np.asarray(crop, dtype=np.float32)
        cache_name = entry["file"]
    current = cache_pixels
    if previous is not None:
        before, old = previous
        unchanged = bool(np.array_equal(old, current))
        a, b = old - old.mean(), current - current.mean()
        height = a.shape[0]
        size = 2 * height
        correlation = np.fft.irfft(
            np.conj(np.fft.rfft(a, n=size, axis=0)) * np.fft.rfft(b, n=size, axis=0),
            n=size, axis=0).sum(axis=1)
        a_energy = (a * a).sum(axis=1)
        b_energy = (b * b).sum(axis=1)
        candidates = []
        for shift in range(min(height // 2, 100)):
            energy = np.sqrt(a_energy[shift:].sum() * b_energy[:height-shift].sum())
            candidates.append(float(correlation[(-shift) % size] / max(energy, 1e-8)))
        best = int(np.argmax(candidates))
        # Synthetic covers are textured; empty placeholders/backgrounds are not.
        # Identical blank frames cannot distinguish scrolling from a pause.
        trackable = min(float(np.abs(np.diff(old, axis=1)).mean()),
                        float(np.abs(np.diff(current, axis=1)).mean())) >= 4
        rows.append(dict(seconds=seconds, interval_ms=entry["elapsedMs"]-before["elapsedMs"],
                         unchanged=unchanged, trackable=trackable, estimated_dip=best*2/factor,
                         confidence=candidates[best], file=entry["file"]))
    previous = entry, current

if len(rows) < 100:
    raise ValueError("Too few captured samples in the timed motion interval.")
stalls, duration = [], 0
for row in rows:
    if row["unchanged"]:
        duration += row["interval_ms"]
    elif duration:
        stalls.append(duration)
        duration = 0
if duration:
    stalls.append(duration)
moving = [row["estimated_dip"] for row in rows if not row["unchanged"]]
summary = dict(samples=len(rows),
               trackable_samples=sum(row["trackable"] for row in rows),
               unchanged_trackable_samples=sum(row["unchanged"] and row["trackable"] for row in rows),
               high_confidence_stationary_samples=sum(row["estimated_dip"] == 0 and row["confidence"] >= .9 for row in rows),
               unchanged_samples=sum(row["unchanged"] for row in rows),
               unchanged_percent=100*sum(row["unchanged"] for row in rows)/max(1,len(rows)),
               longest_unchanged_ms=max(stalls, default=0),
               unchanged_runs_over_50ms=sum(value > 50 for value in stalls),
               mean_confidence=float(np.mean([row["confidence"] for row in rows])),
               median_estimated_dip=float(np.median(moving)) if moving else 0,
               movement_start_in_capture_seconds=movement_start, crop=box)
(root / "captured-motion.json").write_text(json.dumps(dict(summary=summary, samples=rows), indent=2))
print(json.dumps(summary, indent=2))
