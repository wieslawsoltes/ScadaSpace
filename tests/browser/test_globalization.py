"""Dependency-free contracts for the published globalization gate."""
import base64
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from check_globalization import boot_configuration, inspect_publish


class GlobalizationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.framework = self.root / 'wwwroot' / '_framework'
        self.framework.mkdir(parents=True)
        self.data = b'owned-icu-test-fixture'
        self.asset = self.framework / 'icudt.fixture.dat'
        self.asset.write_bytes(self.data)
        self.config = {'globalizationMode': 'all', 'resources': {'icu': [{
            'virtualPath': 'icudt.dat', 'name': self.asset.name,
            'hash': 'sha256-' + base64.b64encode(hashlib.sha256(self.data).digest()).decode('ascii')}]}}
        self.write_loader()

    def write_loader(self, name='dotnet.fixture.js'):
        (self.framework / name).write_text('const boot=(/*json-start*/' + json.dumps(self.config) + '/*json-end*/);')

    def test_full_asset_is_verified_against_its_metadata(self):
        result = inspect_publish(self.root)
        self.assertTrue(result['passed']); self.assertEqual(result['bytes'], len(self.data))
        self.assertEqual(result['sha256'], hashlib.sha256(self.data).hexdigest())

    def test_sharded_and_invariant_configuration_is_rejected(self):
        for mode in ('sharded', 'invariant', 'custom'):
            self.config['globalizationMode'] = mode; self.write_loader()
            with self.assertRaises(ValueError): inspect_publish(self.root)

    def test_missing_or_malformed_boot_configuration_is_rejected(self):
        for text in ('', '/*json-start*/[]/*json-end*/', '/*json-start*/bad/*json-end*/'):
            with self.assertRaises(ValueError): boot_configuration(text)

    def test_incorrect_or_missing_asset_fails_verification(self):
        self.asset.write_bytes(b'corrupted')
        with self.assertRaises(ValueError): inspect_publish(self.root)
        self.asset.unlink()
        with self.assertRaises(OSError): inspect_publish(self.root)

    def test_duplicate_or_unsafe_assets_are_rejected(self):
        entry = self.config['resources']['icu'][0]
        self.config['resources']['icu'].append(dict(entry)); self.write_loader()
        with self.assertRaises(ValueError): inspect_publish(self.root)
        self.config['resources']['icu'] = [entry]
        entry['name'] = '../icudt.dat'; self.write_loader()
        with self.assertRaises(ValueError): inspect_publish(self.root)

    def test_ambiguous_loaders_cannot_pass(self):
        self.write_loader('dotnet.other.js')
        with self.assertRaises(ValueError): inspect_publish(self.root)


if __name__ == '__main__': unittest.main()
