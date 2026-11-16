"""Exercise prepared-artwork acceptance and rejection using disposable packages."""
import importlib.util
import json
import os
import pathlib
import shutil
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch
from PIL import Image

source = pathlib.Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('reuse_artwork', source / 'Reuse-TreeArtwork.py')
reuse = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reuse)


class PreparedArtworkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='pob-prepared-artwork-')
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.package = self.root / 'package'
        self.engine = self.root / 'engine'
        self.output = self.root / 'output'
        self.lock = self.root / 'engine-lock.json'
        self.lock.write_text('{"schemaVersion":1}', encoding='utf-8')
        (self.package / 'payload').mkdir(parents=True)
        (self.package / 'manifest.json').write_text('{"id":"widgetrail.samples.poe1-companion"}', encoding='utf-8')
        shutil.copyfile(self.lock, self.package / 'payload/engine-lock.json')
        for contract in reuse.CONTRACTS:
            target = self.package / 'payload' / contract
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / contract, target)
        tree = self.engine / 'src/TreeData/current'
        tree.mkdir(parents=True)
        (tree / 'tree.lua').write_text('return {}', encoding='utf-8')
        self.art = self.package / 'payload/web/art/current'
        self.art.mkdir(parents=True)
        atlas = {'tile': 1, 'columns': 1, 'sheets': 1, 'icons': {'icon': 0}}
        self.index = {**atlas, 'decorations': atlas, 'layers': {'sheets': 1, 'size': 1024, 'sprites': {}}}
        (self.art / 'index.json').write_text(json.dumps(self.index), encoding='utf-8')
        for name, size in [('nodes-0.webp', 1), ('decorations-0.webp', 1), ('layers-0.webp', 1024)]:
            Image.new('RGBA', (size, size)).save(self.art / name)
        self.seal()

    def seal(self):
        digest, _ = reuse.inventory(self.package)
        (self.package / '.wrail-integrity.json').write_text(json.dumps(
            {'schemaVersion': 1, 'algorithm': reuse.ALGORITHM, 'contentDigest': digest}), encoding='utf-8')

    def run_reuse(self, game=1):
        reuse.reuse(self.package, game, self.engine, self.lock, self.output)

    def test_exact_artwork_is_preserved(self):
        self.run_reuse()
        for path in self.art.iterdir():
            self.assertEqual(path.read_bytes(), (self.output / 'current' / path.name).read_bytes())

    def test_tampered_package_is_rejected(self):
        (self.art / 'nodes-0.webp').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'content digest mismatch'):
            self.run_reuse()
        self.assertFalse(self.output.exists())

    def test_different_game_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'different game'):
            self.run_reuse(2)

    def test_changed_identity_after_inventory_is_rejected(self):
        original_inventory = reuse.inventory
        def change_identity(package):
            result = original_inventory(package)
            (package / 'manifest.json').write_text('{"id":"changed"}', encoding='utf-8')
            return result
        with patch.object(reuse, 'inventory', change_identity):
            with self.assertRaisesRegex(ValueError, 'identity changed after verification'):
                self.run_reuse()
        self.assertFalse(self.output.exists())

    def test_directory_symlink_is_rejected_before_traversal(self):
        target = self.root / 'outside'
        target.mkdir()
        try:
            os.symlink(target, self.package / 'linked', target_is_directory=True)
        except OSError as error:
            self.skipTest('Directory symlinks unavailable: ' + str(error))
        with self.assertRaisesRegex(ValueError, 'link or junction'):
            self.run_reuse()

    @unittest.skipUnless(os.name == 'nt', 'Windows junction boundary')
    def test_junction_is_rejected_before_traversal(self):
        target = self.root / 'outside'
        target.mkdir()
        subprocess.run(['cmd', '/c', 'mklink', '/J', str(self.package / 'linked'), str(target)], check=True, capture_output=True)
        with self.assertRaisesRegex(ValueError, 'link or junction'):
            self.run_reuse()

    def test_changed_engine_lock_is_rejected(self):
        self.lock.write_text('{}', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'engine lock mismatch'):
            self.run_reuse()

    def test_changed_renderer_contract_is_rejected(self):
        (self.package / 'payload/web/tree.js').write_text('changed', encoding='utf-8')
        self.seal()
        with self.assertRaisesRegex(ValueError, 'contract mismatch'):
            self.run_reuse()

    def test_missing_tree_version_is_rejected(self):
        other = self.engine / 'src/TreeData/other'
        other.mkdir()
        (other / 'tree.lua').write_text('return {}', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'tree version set mismatch'):
            self.run_reuse()

    def test_invalid_resource_bounds_are_rejected(self):
        self.index['icons']['icon'] = 100
        (self.art / 'index.json').write_text(json.dumps(self.index), encoding='utf-8')
        self.seal()
        with self.assertRaises(reuse.subprocess.CalledProcessError):
            self.run_reuse()
        self.assertFalse(self.output.exists())

    def test_changed_bytes_during_copy_are_rejected(self):
        original_copy = shutil.copyfile
        def corrupt_copy(src, dst):
            original_copy(src, dst)
            pathlib.Path(dst).write_bytes(b'changed during copy')
        with patch.object(reuse.shutil, 'copyfile', corrupt_copy):
            with self.assertRaisesRegex(ValueError, 'Copied artwork hash mismatch'):
                self.run_reuse()


if __name__ == '__main__':
    unittest.main()
