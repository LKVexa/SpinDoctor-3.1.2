"""Finalize 3.1.2 carriers: rename artwork-updated Lunar127 carriers to SpinDoctor-3.1.2.* and merge proofs.
Usage: python finalize_carriers.py <pre-artwork-dir> <artwork-dir> <final-dir> <version>"""
import hashlib, json, shutil, sys
from pathlib import Path
pre, art, final, version = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4]
if final.exists(): raise SystemExit('final carrier directory must be new')
final.mkdir(parents=True)
stem = 'Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE'; out = 'SpinDoctor-' + version
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
before = json.loads((pre / 'build-validation.json').read_text(encoding='utf-8-sig'))
artwork = json.loads((art / 'artwork-validation.json').read_text(encoding='utf-8-sig'))
assert artwork['status'] == 'PASS' and artwork['version'] == version
assert artwork['oldGifSha256'] == before['gifSha256'] and artwork['oldTiffSha256'] == before['tiffSha256']
assert artwork['streamSha256'] == before['streamSha256']
for suffix in ('tiff', 'gif'):
    shutil.copy2(art / f'{stem}.{suffix}', final / f'{out}.{suffix}')
    assert sha(final / f'{out}.{suffix}') == artwork[suffix + 'Sha256']
shutil.copy2(art / f'SpinDoctor-{version}-Title-Card.png', final / f'SpinDoctor-{version}-Title-Card.png')
merged = dict(before)
merged.update({k: artwork[k] for k in ('tiffSha256', 'gifSha256', 'coverPixelRgbSha256', 'matchingCoverDecodedPixels', 'payloadStreamUnchanged',
    'gifArtworkOutsideVersionFooterUnchanged', 'gifDataFrameBytesUnchanged', 'tiffOriginalBytesExceptFirstIfdPointerUnchanged', 'gifLoopAndFrameDelaysUnchanged')})
merged.update({'tiffCoverDecodedPixelsUnchanged': False, 'gifCoverDecodedPixelsUnchanged': False, 'gifCoverPrefixBytesUnchanged': False,
    'coverArtworkUpdated': True, 'coverVersion': version, 'carrierBasename': out,
    'artworkScope': 'Version footer only; GIF/TIFF title cards match; rebuilt executable stream and encoded data frames preserved by artwork update.'})
w = lambda p, v: p.write_text(json.dumps(v, indent=2) + '\n', encoding='utf-8', newline='\n')
w(final / 'build-validation-before-artwork.json', before); w(final / 'artwork-validation.json', artwork); w(final / 'build-validation.json', merged)
print(json.dumps({'status': 'PASS', 'tiff': merged['tiffSha256'], 'gif': merged['gifSha256'], 'entries': merged['entries']}))
