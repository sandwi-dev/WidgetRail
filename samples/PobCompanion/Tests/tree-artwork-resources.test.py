"""Audit the actual generated browser resources, independent of encoder limits."""
import json,pathlib,sys
from PIL import Image
root=pathlib.Path(sys.argv[1])
count=0
for path in sorted(root.glob('*/index.json')):
    data=json.loads(path.read_text(encoding='utf-8'));folder=path.parent
    files=list(folder.iterdir())
    assert len(files)+4<=32,(path,'resource count')
    assert sum(f.stat().st_size for f in files)<=1900*1024,(path,'aggregate')
    assert all(f.stat().st_size<=256*1024 for f in files),(path,'per-resource')
    decoded=0
    for kind,atlas in [('nodes',data),('decorations',data['decorations'])]:
        per=atlas['columns']**2
        assert all(isinstance(i,int) and 0<=i<atlas['sheets']*per for i in atlas['icons'].values())
        for i in range(atlas['sheets']):
            with Image.open(folder/f'{kind}-{i}.webp')as image:
                assert image.size==(atlas['columns']*atlas['tile'],)*2
                decoded+=image.width*image.height*4
    atlas=data['layers'];assert atlas['sheets']<=8
    for i in range(atlas['sheets']):
        with Image.open(folder/f'layers-{i}.webp')as image:
            assert image.size==(1024,1024);decoded+=image.width*image.height*4
    for key,sprite in atlas['sprites'].items():
        assert 0<=sprite['sheet']<atlas['sheets'] and min(sprite['x'],sprite['y'])>=0
        assert sprite['w']>0 and sprite['h']>0 and sprite['x']+sprite['w']<=1024 and sprite['y']+sprite['h']<=1024
        ratio=sprite['width']/sprite['height']
        assert abs(sprite['w']-ratio*sprite['h'])<=ratio+1,(key,'aspect ratio rounded to pixels')
    assert decoded<=52*1024*1024
    count+=1
    print(path.parent.name,'PASS',len(files),'resources',sum(f.stat().st_size for f in files),'bytes',decoded,'decoded bytes')
assert count>0,'No generated resources audited'
