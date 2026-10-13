"""Validate the published .NET boot metadata and actual ICU bytes, not just MSBuild intent."""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
from pathlib import Path

START, END = '/*json-start*/', '/*json-end*/'


def boot_configuration(text: str) -> dict:
    if text.count(START) != 1 or text.count(END) != 1:
        raise ValueError('Expected one embedded .NET boot configuration.')
    start, end = text.index(START) + len(START), text.index(END)
    if end <= start:
        raise ValueError('Invalid .NET boot configuration boundaries.')
    value = json.loads(text[start:end])
    if not isinstance(value, dict):
        raise ValueError('Boot configuration must be an object.')
    return value


def inspect_publish(directory: Path) -> dict:
    framework = directory / 'wwwroot' / '_framework'
    candidates = []
    for path in framework.glob('dotnet*.js'):
        text = path.read_text(encoding='utf-8')
        if START in text:
            candidates.append((path, boot_configuration(text)))
    if len(candidates) != 1:
        raise ValueError('Expected one published .NET loader with embedded configuration.')
    loader, config = candidates[0]
    if config.get('globalizationMode') != 'all':
        raise ValueError('Dynamic locales require full ICU; published mode is ' + str(config.get('globalizationMode')))
    resources = config.get('resources', {})
    icu = resources.get('icu', []) if isinstance(resources, dict) else []
    if not isinstance(icu, list) or len(icu) != 1 or not isinstance(icu[0], dict) or icu[0].get('virtualPath') != 'icudt.dat':
        raise ValueError('Published boot metadata must select one full icudt.dat asset.')
    entry = icu[0]
    name = entry.get('name')
    if not isinstance(name, str) or not name or '/' in name or '\\' in name or Path(name).name != name:
        raise ValueError('ICU asset must have a safe local filename.')
    asset = framework / name
    raw = asset.read_bytes()
    checksum = hashlib.sha256(raw).digest()
    if not raw or entry.get('hash') != 'sha256-' + base64.b64encode(checksum).decode('ascii'):
        raise ValueError('Published ICU bytes do not match their boot metadata hash.')
    result = {'passed': True, 'mode': 'all', 'loader': loader.name, 'asset': name,
              'virtualPath': 'icudt.dat', 'bytes': len(raw), 'sha256': checksum.hex()}
    for extension in ('br', 'gz'):
        compressed = asset.with_name(asset.name + '.' + extension)
        if compressed.exists(): result[extension + 'Bytes'] = compressed.stat().st_size
    return result


def main() -> int:
    parser = argparse.ArgumentParser(); parser.add_argument('publish_directory', type=Path)
    directory = parser.parse_args().publish_directory
    output = Path('TestResults/browser-globalization'); output.mkdir(parents=True, exist_ok=True)
    try:
        report = inspect_publish(directory)
    except (ValueError, OSError, UnicodeError) as error:
        report = {'passed': False, 'error': str(error)}
    text = json.dumps(report, indent=2)
    (output / 'report.json').write_text(text)
    print(text, flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
