"""Retain verified installed artwork for an update with unchanged tree contracts."""
import argparse
import hashlib
import json
import pathlib
import shutil
import stat
import struct
import subprocess
import sys

SOURCE = pathlib.Path(__file__).resolve().parent
CONTRACTS = ('web/tree.js', 'web/tree.css', 'web/tree.html',
             'Engine/tree-artwork.lua', 'Engine/tree-presentation.lua', 'Engine/tree-geometry.lua')
ALGORITHM = 'sha256-content-tree-v1'


def inventory(package):
    digest = hashlib.sha256(ALGORITHM.encode('utf-8'))
    entries = {}
    pending, files = [package], []
    while pending:
        path = pending.pop()
        info = path.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError('Prepared package contains a link or junction')
        if stat.S_ISDIR(info.st_mode):
            pending.extend(path.iterdir())
        elif stat.S_ISREG(info.st_mode):
            files.append(path)
        else:
            raise ValueError('Prepared package contains a non-regular file')
    for path in sorted(files, key=lambda p: p.relative_to(package).as_posix().encode('utf-16-be')):
        relative = path.relative_to(package).as_posix()
        if relative == '.wrail-integrity.json':
            continue
        name = relative.encode('utf-8')
        size = path.stat().st_size
        digest.update(struct.pack('>q', len(name)))
        digest.update(name)
        digest.update(struct.pack('>q', size))
        file_hash = hashlib.sha256()
        actual = 0
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                actual += len(chunk)
                digest.update(chunk)
                file_hash.update(chunk)
        if actual != size:
            raise ValueError('Prepared package changed while being read')
        entries[relative] = file_hash.hexdigest()
    return digest.hexdigest(), entries


def reuse(package, game, engine, engine_lock, output):
    package, engine, engine_lock, output = map(pathlib.Path, (package, engine, engine_lock, output))
    if output.exists():
        raise ValueError('Prepared artwork output must be new')
    seal = json.loads((package / '.wrail-integrity.json').read_text(encoding='utf-8-sig'))
    if seal.get('schemaVersion') != 1 or seal.get('algorithm') != ALGORITHM:
        raise ValueError('Unsupported prepared package integrity metadata')
    digest, entries = inventory(package)
    if digest != seal.get('contentDigest'):
        raise ValueError('Prepared package content digest mismatch')
    manifest_bytes = (package / 'manifest.json').read_bytes()
    if hashlib.sha256(manifest_bytes).hexdigest() != entries.get('manifest.json'):
        raise ValueError('Prepared package identity changed after verification')
    manifest = json.loads(manifest_bytes.decode('utf-8-sig'))
    if manifest.get('id') != f'widgetrail.samples.poe{game}-companion':
        raise ValueError('Prepared artwork belongs to a different game')
    if entries.get('payload/engine-lock.json') != hashlib.sha256(engine_lock.read_bytes()).hexdigest():
        raise ValueError('Prepared artwork engine lock mismatch')
    for contract in CONTRACTS:
        if entries.get('payload/' + contract) != hashlib.sha256((SOURCE / contract).read_bytes()).hexdigest():
            raise ValueError('Prepared artwork contract mismatch: ' + contract)
    prefix = 'payload/web/art/'
    artwork = {name[len(prefix):]: value for name, value in entries.items() if name.startswith(prefix)}
    required = {path.parent.name for path in (engine / 'src/TreeData').glob('*/tree.lua')}
    versions = {str(pathlib.PurePosixPath(name).parent) for name in artwork if name.endswith('/index.json')}
    if not required or versions != required:
        raise ValueError('Prepared artwork tree version set mismatch')
    if any(len(pathlib.PurePosixPath(name).parts) != 2 or name.split('/')[0] not in versions for name in artwork):
        raise ValueError('Prepared artwork contains unexpected paths')
    audit = SOURCE / 'Tests/tree-artwork-resources.test.py'
    subprocess.run([sys.executable, '-I', str(audit), str(package / 'payload/web/art')], check=True)
    for name, expected_hash in artwork.items():
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(package / prefix / name, target)
        if hashlib.sha256(target.read_bytes()).hexdigest() != expected_hash:
            raise ValueError('Copied artwork hash mismatch: ' + name)
    subprocess.run([sys.executable, '-I', str(audit), str(output)], check=True)
    print(json.dumps({'preparedArtworkPackage': str(package.resolve()), 'contentDigest': digest,
                      'game': game, 'versions': len(versions), 'files': len(artwork)}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True)
    parser.add_argument('--game', required=True, type=int, choices=(1, 2))
    parser.add_argument('--engine', required=True)
    parser.add_argument('--engine-lock', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    reuse(args.package, args.game, args.engine, args.engine_lock, args.output)
