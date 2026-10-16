"""Prepare bounded, deduplicated browser atlases from the pinned upstream Git tree.

Requires Pillow 12.3.0 and zstandard 0.25.0. These are build dependencies only.
Artwork stays local; the widget never downloads or decodes DDS at runtime.
"""
import argparse, hashlib, io, json, math, os, pathlib, struct, subprocess
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('--engine', required=True)
parser.add_argument('--runtime', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
engine, runtime, output = map(pathlib.Path, (args.engine, args.runtime, args.output))
output.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, LUA_PATH=str(engine/'runtime/lua/?.lua')+';;')
tile, columns = 32, 16

def git_file(relative):
    return subprocess.check_output(['git','-C',str(engine),'show','HEAD:'+relative])

def dds_layers(data):
    import zstandard
    raw = zstandard.ZstdDecompressor().decompress(data, max_output_size=256*1024*1024)
    if raw[:4] != b'DDS ' or raw[84:88] != b'DX10':
        raise ValueError('Expected a DX10 DDS array')
    height, width = struct.unpack_from('<II', raw, 12)
    mipmaps = max(1, struct.unpack_from('<I',raw,28)[0])
    fmt, dimension, misc, count, flags = struct.unpack_from('<IIIII', raw,128)
    if fmt not in (71,72) or dimension != 3 or not 0<count<10000:
        raise ValueError('Unexpected upstream skill texture format')
    stride=sum(max(1,((max(1,width>>m)+3)//4))*max(1,((max(1,height>>m)+3)//4))*8 for m in range(mipmaps))
    if len(raw) != 148+stride*count: raise ValueError('DDS array size mismatch')
    header=bytearray(raw[:148]);struct.pack_into('<I',header,140,1)
    return [Image.open(io.BytesIO(header+raw[148+i*stride:148+(i+1)*stride])).convert('RGBA') for i in range(count)]

for directory in sorted((engine/'src/TreeData').iterdir()):
    if not (directory/'tree.lua').is_file(): continue
    metadata=json.loads(subprocess.check_output([str(runtime/'luajit.exe'),str(pathlib.Path(__file__).with_name('Export-TreeArtwork.lua')),str(directory)],env=env))
    images, digests = [], {}
    by_file={}
    for key,rect in metadata.items(): by_file.setdefault(rect['file'],[]).append((key,rect))
    current={}
    for filename,entries in sorted(by_file.items()):
        raw=git_file('src/TreeData/'+directory.name+'/'+filename)
        layers=dds_layers(raw) if filename.endswith('.dds.zst') else None
        sheet=None if layers else Image.open(io.BytesIO(raw)).convert('RGBA')
        for key,rect in sorted(entries):
            if layers:
                # PoB's renderer exposes DDS array slices as one-based indices.
                if not 1 <= rect['layer'] <= len(layers): raise ValueError('Invalid DDS layer')
                image=layers[rect['layer']-1]
            else:
                x,y,w,h=(rect[k] for k in ('x','y','w','h'))
                if min(x,y)<0 or min(w,h)<=0 or x+w>sheet.width or y+h>sheet.height: raise ValueError('Sprite outside atlas')
                image=sheet.crop((x,y,x+w,y+h))
            image=image.resize((tile,tile),Image.Resampling.LANCZOS)
            digest=hashlib.sha256(image.tobytes()).hexdigest()
            if digest not in digests: digests[digest]=len(images);images.append(image)
            current[key]=digests[digest]
    per_sheet=columns*columns
    count=math.ceil(len(images)/per_sheet)
    if count>10: raise ValueError('Artwork exceeds the packaged-session resource budget')
    destination=output/directory.name;destination.mkdir(exist_ok=True)
    for index in range(count):
        atlas=Image.new('RGB',(columns*tile,columns*tile),(16,18,20))
        for offset,image in enumerate(images[index*per_sheet:(index+1)*per_sheet]):
            atlas.paste(image,((offset%columns)*tile,(offset//columns)*tile),image.getchannel('A'))
        atlas.save(destination/f'nodes-{index}.jpg',quality=72,optimize=True,subsampling=0)
    (destination/'index.json').write_text(json.dumps(dict(tile=tile,columns=columns,sheets=count,icons=current),separators=(',',':')),encoding='utf-8')
    files=list(destination.iterdir())
    if any(p.stat().st_size>256*1024 for p in files) or sum(p.stat().st_size for p in files)>900*1024:
        raise ValueError('Tree artwork exceeds the bounded web-session transfer')
    print(directory.name,len(current),'icons;',sum(p.stat().st_size for p in files),'bytes',flush=True)
print('Total bytes',sum(p.stat().st_size for p in output.rglob('*') if p.is_file()),flush=True)
