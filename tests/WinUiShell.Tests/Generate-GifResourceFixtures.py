# Generated test media only; requires Pillow in an isolated Python environment.
import argparse
from io import BytesIO
from pathlib import Path
import json
import struct
from PIL import Image, __version__

parser = argparse.ArgumentParser(description='Generate bounded adversarial GIF fixtures without downloaded media.')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--endurance', action='store_true', help='Generate finite two-minute animations with a unique terminal frame.')
args = parser.parse_args()
root = args.output
root.mkdir(parents=True, exist_ok=True)

def blocks_end(data, pos):
    while data[pos]:
        pos += 1 + data[pos]
    return pos + 1

def generate(name, size, frames, canvas=None, terminal=False, disposal=1):
    palette = [255, 0, 0, 0, 0, 255, 0, 255, 0] + [0] * (768 - 9)
    colors = [Image.new('P', (size, size), color) for color in range(3 if terminal else 2)]
    for color in colors:
        color.putpalette(palette)
    output = BytesIO()
    colors[0].save(output, format='GIF', save_all=True, append_images=colors[1:],
                   duration=100, disposal=disposal, optimize=False, **({} if terminal else {'loop': 0}))
    data = output.getvalue()
    pos = 13 + (3 * (1 << ((data[10] & 7) + 1)) if data[10] & 128 else 0)
    prefix = bytearray(data[:pos])
    pending = b''
    records = []
    while data[pos] != 59:
        start = pos
        if data[pos] == 33:
            pos = blocks_end(data, pos + 2)
            if data[start + 1] == 249:
                pending = data[start:pos]
            else:
                prefix.extend(data[start:pos])
        elif data[pos] == 44:
            pos += 10
            if data[start + 9] & 128:
                pos += 3 * (1 << ((data[start + 9] & 7) + 1))
            pos = blocks_end(data, pos + 1)
            record = bytearray(data[start:pos])
            if canvas:
                offset = (canvas - size) // 2
                record[1:5] = struct.pack('<HH', offset, offset)
            records.append(pending + record)
            pending = b''
        else:
            raise ValueError('Unexpected generated GIF block')
    assert len(records) == (3 if terminal else 2)
    if canvas:
        prefix[6:10] = struct.pack('<HH', canvas, canvas)
    encoded = bytes(prefix) + b''.join(records[i % 2] for i in range(frames - int(terminal)))
    encoded += (records[2] if terminal else b'') + b';'
    assert len(encoded) <= 8 * 1024 * 1024
    path = root / (name + '.gif')
    path.write_bytes(encoded)
    with Image.open(path) as check:
        assert check.n_frames == frames
        assert check.size == (canvas or size, canvas or size)
        # Records repeat byte-for-byte. Independently decode the two different
        # frames; do not composite ten thousand 4096px canvases in the generator.
        check.seek(1)
        assert check.convert('RGB').getpixel(((canvas or size)//2, (canvas or size)//2)) == (0, 0, 255)
        if terminal:
            assert 'loop' not in check.info
            # Decode the distinct terminal record independently: traversing the
            # complete sequence is the native renderer's endurance test.
            with Image.open(BytesIO(bytes(prefix) + records[2] + b';')) as end:
                assert end.convert('RGB').getpixel(((canvas or size)//2, (canvas or size)//2)) == (0, 255, 0)
    return dict(name=name, file=str(path.resolve()), frames=frames, frameSide=size,
                canvasSide=canvas or size, encodedBytes=len(encoded), disposal=disposal,
                terminalMilliseconds=(frames - 1) * 100 if terminal else 0)

cases = ([generate('endurance-full-1200', 1024, 1200, terminal=True),
          generate('endurance-restore-deltas-1200', 64, 1200, 4096, terminal=True, disposal=3)] if args.endurance else
         [generate('tiny-10000', 1, 10000), generate('standard-120', 512, 120),
         generate('large-600', 1024, 600), generate('large-canvas-deltas-10000', 64, 10000, 4096)]
        )
(root / 'cases.json').write_text(json.dumps(dict(generator='Pillow '+__version__, cases=cases), indent=2))
print(json.dumps(cases, indent=2))
