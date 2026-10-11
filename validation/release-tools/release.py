"""Frozen-source sealing, clean assembly, and verified release packaging.

Run seal after concurrent source edits finish. Never copies a runtime workspace
or mutates the supplied baseline application.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import zipfile

STEM = 'SpinDoctor-3.1.2'
VERSION = '3.1.2'


def digest(path: Path) -> str:
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')


def files(root: Path) -> list[Path]:
    result = []
    for path in sorted(root.rglob('*')):
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise RuntimeError('Linked release input refused: ' + str(path))
        if '__pycache__' in path.parts or path.suffix.lower() == '.pyc':
            raise RuntimeError('Transient Python cache in frozen tree: ' + str(path))
        if path.is_file():
            result.append(path)
    return result


def verify_source(tree: Path) -> dict:
    immutable = json.loads((tree / 'integrity/immutable.json').read_text(encoding='utf-8-sig'))
    expected = immutable['sha256']
    current = {p.relative_to(tree).as_posix() for p in files(tree / 'mvp')}
    if current != set(expected):
        raise RuntimeError('Immutable source inventory differs: ' + str(current ^ set(expected)))
    for name, value in expected.items():
        canonical = PurePosixPath(name)
        if canonical.is_absolute() or '..' in canonical.parts or '\\' in name or not name.startswith('mvp/'):
            raise RuntimeError('Noncanonical source manifest name: ' + name)
        if digest(tree / name) != value:
            raise RuntimeError('Frozen source checksum mismatch: ' + name)
    model = json.loads((tree / 'mvp/quantum/MODEL.json').read_text(encoding='utf-8-sig'))
    actual_version = (tree / 'mvp/VERSION.txt').read_text(encoding='utf-8-sig').strip()
    terminal_source = (tree / 'mvp/quantum/QuantumTerminal.cs').read_text(encoding='utf-8-sig')
    match = re.search(r'ProductVersion\s*=\s*"([^"\r\n]+)"', terminal_source)
    entry = json.loads((tree / 'mvp/meta/ENTRYPOINT.json').read_text(encoding='utf-8-sig'))
    presets = json.loads((tree / 'mvp/SPINDOCTOR_PRESETS.json').read_text(encoding='utf-8-sig'))
    if actual_version != VERSION or model.get('release') != VERSION or entry.get('release') != VERSION or presets.get('release') != VERSION or not match or match.group(1) != VERSION:
        raise RuntimeError('Runtime version, VERSION, MODEL, ENTRYPOINT, and preset releases must agree.')
    for name, value in model['sourceSha256'].items():
        source = tree / 'mvp/quantum' / name
        if not source.is_file():
            source = tree / 'mvp' / name
        if digest(source) != value:
            raise RuntimeError('MODEL source checksum mismatch: ' + name)
    html = (tree / 'mvp/QUANTUM_TERMINAL.html').read_text(encoding='utf-8-sig')
    ui_match = re.search(r'^const presetManifest=(.+);$', html, re.M)
    if not ui_match:
        raise RuntimeError('Embedded preset manifest is missing.')
    ui = json.loads(ui_match[1])
    if ui.get('sourceSha256') != digest(tree / 'mvp/SPINDOCTOR_PRESETS.json'):
        raise RuntimeError('Embedded preset source hash differs from the native catalog.')
    if len(ui['presets']) != 241 or [(p['id'], p['commands']) for p in ui['presets']] != [(p['id'], p['commands']) for p in presets['presets']]:
        raise RuntimeError('Embedded preset commands differ from the native catalog.')
    if html != (tree / 'mvp/SpinDoctor.html').read_text(encoding='utf-8-sig'):
        raise RuntimeError('Terminal HTML mirrors differ.')
    return {'status': 'PASS', 'version': VERSION, 'immutableFiles': len(expected), 'modelSourceHashes': len(model['sourceSha256'])}


def seal(tree: Path, baseline: Path | None) -> dict:
    mvp = tree / 'mvp'
    model_path = mvp / 'quantum/MODEL.json'
    model = json.loads(model_path.read_text(encoding='utf-8-sig'))
    model['release'] = VERSION
    # Freeze the catalog first, then bind its exact bytes in both UI mirrors
    # before calculating the MODEL source hashes and immutable payload bank.
    presets_path = mvp / 'SPINDOCTOR_PRESETS.json'
    presets = json.loads(presets_path.read_text(encoding='utf-8-sig'))
    presets['release'] = VERSION
    write_json(presets_path, presets)
    html_path = mvp / 'QUANTUM_TERMINAL.html'
    html = html_path.read_text(encoding='utf-8-sig')
    ui_match = re.search(r'^const presetManifest=(.+);$', html, re.M)
    if not ui_match:
        raise RuntimeError('Embedded preset manifest is missing.')
    ui = json.loads(ui_match[1]); ui['sourceSha256'] = digest(presets_path)
    html = html[:ui_match.start(1)] + json.dumps(ui, ensure_ascii=False, separators=(',', ':')) + html[ui_match.end(1):]
    for name in ('QUANTUM_TERMINAL.html', 'SpinDoctor.html'):
        (mvp / name).write_text(html, encoding='utf-8', newline='')
    model['attachments']['workflow'] = ('Upload preserves source bytes and SHA-256; preview, explicit import/run. '
        'PDF 4 MiB, text 5 MiB, 32 preserved sources, 30 MiB total. Folder inputs are bounded '
        '4 MiB per file, at most 128 files (.json .qasm .qasm2 .txt .circuit .csv .pdf; other files are reported as skipped), nonrecursive, one file at a time; each compiled file '
        'may contain at most 128 ordinary commands. The per-file limit is not a whole-folder command cap.')
    model['sequences']['folder'] = ('q.folder run path="D:/folder" downloads=false purge=true verify=true samples=10240. '
        'Automatic q.verify samples runs after each file finishes and before success receipts are '
        'published or temporary results are purged. It materializes sampled current procedural-model '
        'predictions on the CPU and OpenCL GPU: amplitudes, spin and Born probabilities. A matching '
        'completed native comparison with consistent sample counts and unchanged descriptor is '
        'required. Failure/cancellation retains outputs and exposes clearly labeled diagnostics. '
        'Verification is separate from input-command counts and does not independently certify '
        'q.math/circuit answers, unsampled states or physical RAM/XOR. Explicit verify=false receipts '
        'are labeled unverified. Requested samples: 1..16384000000 (default10240); native boundary samples may add points '
        'and require waiting for the matching asynchronous job. Wide integer requests stream in16384-point batches; '
        'the raised request ceiling does not increase batch memory. Individual downloads are optional. '
        'Sources/seeds/checkpoints/history are preserved; transient references are released only '
        'after verified receipt capture. Actual sample preview is bounded to32 rows; scope, counts, '
        'descriptor hash, seed/digest, residuals, native completion and archive/purge times are retained.')
    for name in model['sourceSha256']:
        source = mvp / 'quantum' / name
        if not source.is_file():
            source = mvp / name
        model['sourceSha256'][name] = digest(source)
    write_json(model_path, model)
    entry_path = mvp / 'meta/ENTRYPOINT.json'
    entry = json.loads(entry_path.read_text(encoding='utf-8-sig'))
    entry['release'] = VERSION
    entry['parentRelease'] = 'SpinDoctor 3.1.1-candidate'
    write_json(entry_path, entry)
    presets_path = mvp / 'SPINDOCTOR_PRESETS.json'
    presets = json.loads(presets_path.read_text(encoding='utf-8-sig'))
    presets['release'] = VERSION
    write_json(presets_path, presets)
    (mvp / 'VERSION.txt').write_text(VERSION + '\n', encoding='utf-8', newline='\n')
    sums = mvp / 'SHA256SUMS.txt'
    payload = files(mvp)
    sums.write_text(''.join(f'{digest(p)}  {p.relative_to(tree).as_posix()}\n' for p in payload if p != sums), encoding='utf-8', newline='\n')
    hashes = {p.relative_to(tree).as_posix(): digest(p) for p in files(mvp)}
    old = json.loads(baseline.read_text(encoding='utf-8-sig'))['sha256'] if baseline else {}
    removed = sorted(set(old) - set(hashes))
    if removed:
        raise RuntimeError('Baseline payload files removed: ' + str(removed))
    write_json(tree / 'integrity/immutable.json', {'schema': 'TIFF-GIF-HOLO/IMMUTABLE/1', 'sha256': hashes})
    result = verify_source(tree)
    result.update(changedEntries=[n for n in hashes if n in old and old[n] != hashes[n]], addedEntries=[n for n in hashes if n not in old], removedEntries=removed)
    return result


def copytree(source: Path, target: Path) -> None:
    for path in files(source):
        dest = target / path.relative_to(source)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, dest)


def assemble(tree: Path, baseline_app: Path, carriers: Path, release: Path, tools: Path) -> dict:
    proof = verify_source(tree)
    runtime = release / 'SpinDoctor'
    if (runtime / 'workspace').exists():
        raise RuntimeError('Assembly target already has a runtime workspace; use a clean release folder.')
    runtime.mkdir(parents=True, exist_ok=True)
    copytree(tree, release / 'source')
    for path in files(tree / 'mvp'):
        if path.parent == tree / 'mvp':
            shutil.copy2(path, runtime / path.name)
    copytree(tree / 'mvp/native', runtime / 'native')
    bootstrap = tree / 'mvp/bootstrap'
    shutil.copy2(bootstrap / 'BOOT.cmd', runtime / 'BOOT.cmd')
    (runtime / 'harness').mkdir(exist_ok=True)
    shutil.copy2(bootstrap / 'TIFFGifHoloHarness.cs', runtime / 'harness/TIFFGifHoloHarness.cs')
    (runtime / 'cartridge').mkdir(exist_ok=True)
    for suffix in ('tiff', 'gif'):
        shutil.copy2(carriers / f'{STEM}.{suffix}', runtime / 'cartridge' / f'{STEM}.{suffix}')
    # External shortcuts verify the carrier. Embedded launchers compile from the
    # verified projection and intentionally differ from these two wrappers.
    wrapper = '@echo off\r\nrem External shortcut verifies and materializes the carrier before launching.\r\ncall "%~dp0BOOT.cmd" %*\r\n'
    for name in ('RUN_SPINDOCTOR.cmd', 'RUN_QUANTUM_XPU.cmd'):
        (runtime / name).write_text(wrapper, encoding='utf-8', newline='')
    license_path = baseline_app / 'LICENSE_GPL3_RUSSELL_PHILIP_SMITHSON'
    if license_path.is_file():
        shutil.copy2(license_path, runtime / license_path.name)
    shutil.copy2(tree / 'mvp/QUANTUM_TERMINAL.html', runtime / 'OPEN_SIMULATOR.html')
    shutil.copy2(tree / 'mvp/SPINDOCTOR-NOTES.txt', runtime / 'README-2ADIC-SHEET.txt')
    copytree(tools, release / 'validation/release-tools')
    shutil.copy2(carriers / 'build-validation.json', release / 'validation/build-validation.json')
    for name in ('artwork-validation.json', 'build-validation-before-artwork.json'):
        if (carriers / name).is_file():
            shutil.copy2(carriers / name, release / 'validation' / name)
    cover = carriers / ('SpinDoctor-' + VERSION.removesuffix('-candidate') + '-Title-Card.png')
    if cover.is_file():
        (release / 'Artwork').mkdir(exist_ok=True)
        shutil.copy2(cover, release / 'Artwork' / cover.name)
    verify_runtime(release)
    proof.update(cleanRuntimeWorkspace=True, externalTerminalShortcutsVerifyCarrier=True,
                 sourceFiles=len(files(tree)), runtimeFiles=len(files(runtime)))
    write_json(release / 'validation/release-consistency.json', proof)
    return proof


def verify_runtime(release: Path) -> None:
    tree, runtime = release / 'source', release / 'SpinDoctor'
    for source in files(tree / 'mvp'):
        if source.parent == tree / 'mvp' and source.name not in ('RUN_SPINDOCTOR.cmd', 'RUN_QUANTUM_XPU.cmd'):
            if digest(source) != digest(runtime / source.name):
                raise RuntimeError('Visible mirror mismatch: ' + source.name)
    for name in ('BOOT.cmd', 'TIFFGifHoloHarness.cs'):
        target = runtime / ('harness' if name.endswith('.cs') else '') / name
        if digest(tree / 'mvp/bootstrap' / name) != digest(target):
            raise RuntimeError('Bootstrap mirror mismatch: ' + name)
    for source in files(tree / 'mvp/native'):
        target = runtime / 'native' / source.relative_to(tree / 'mvp/native')
        if digest(source) != digest(target):
            raise RuntimeError('Optional native source mirror mismatch: ' + source.name)
    for name in ('RUN_SPINDOCTOR.cmd', 'RUN_QUANTUM_XPU.cmd'):
        expected = '@echo off\r\nrem External shortcut verifies and materializes the carrier before launching.\r\ncall "%~dp0BOOT.cmd" %*\r\n'.encode('utf-8')
        if (runtime / name).read_bytes() != expected:
            raise RuntimeError('External terminal shortcut differs from verified wrapper: ' + name)
    proof = json.loads((release / 'validation/build-validation.json').read_text(encoding='utf-8-sig'))
    flags = ['bothCarriersVerifiedWithHardenedReader', 'allPayloadBytesRoundTripIdentically', 'immutablePayloadBankVerified', 'checkpointHeaderUnchanged']
    if proof.get('coverArtworkUpdated') is True:
        if proof.get('coverVersion') != VERSION.removesuffix('-candidate'):
            raise RuntimeError('Carrier artwork version differs from runtime release.')
        flags += ['matchingCoverDecodedPixels', 'payloadStreamUnchanged', 'gifArtworkOutsideVersionFooterUnchanged',
                  'gifDataFrameBytesUnchanged', 'tiffOriginalBytesExceptFirstIfdPointerUnchanged', 'gifLoopAndFrameDelaysUnchanged']
        artwork = json.loads((release / 'validation/artwork-validation.json').read_text(encoding='utf-8-sig'))
        for field in ('gifSha256', 'tiffSha256', 'streamSha256'):
            if proof.get(field) != artwork.get(field):
                raise RuntimeError('Artwork proof differs from carrier build proof: ' + field)
    else:
        flags += ['tiffCoverDecodedPixelsUnchanged', 'gifCoverDecodedPixelsUnchanged', 'gifCoverPrefixBytesUnchanged']
    for flag in flags:
        if proof.get(flag) is not True:
            raise RuntimeError('Missing or failed carrier proof: ' + flag)
    for suffix in ('tiff', 'gif'):
        if digest(runtime / 'cartridge' / f'{STEM}.{suffix}') != proof.get(suffix + 'Sha256'):
            raise RuntimeError('Delivered carrier differs from rebuilt verified carrier: ' + suffix)
    if digest(tree / 'integrity/immutable.json') != proof.get('immutableSha256'):
        raise RuntimeError('Delivered source immutable manifest differs from verified carrier payload.')
    if proof.get('entries') != len(files(tree / 'mvp')) + 1:
        raise RuntimeError('Carrier payload inventory differs from readable source.')


def package(release: Path, archive: Path, scratch: Path) -> dict:
    verify_source(release / 'source')
    verify_runtime(release)
    if (release / 'SpinDoctor/workspace').exists():
        raise RuntimeError('Runtime workspace must not be shipped.')
    manifest_path = release / 'SHA256SUMS.txt'
    inventory = files(release)
    hashes = {p.relative_to(release).as_posix(): digest(p) for p in inventory if p != manifest_path}
    manifest_path.write_text(''.join(f'{value}  {name}\n' for name, value in hashes.items()), encoding='utf-8', newline='\n')
    hashes['SHA256SUMS.txt'] = digest(manifest_path)
    scratch.mkdir(parents=True, exist_ok=True)
    archive.parent.mkdir(parents=True, exist_ok=True)
    temp = scratch / (archive.name + '.tmp')
    with zipfile.ZipFile(temp, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as zipped:
        for name in sorted(hashes):
            zipped.write(release / name, release.name + '/' + name)
    with zipfile.ZipFile(temp) as zipped:
        if zipped.testzip() is not None:
            raise RuntimeError('ZIP CRC verification failed')
        names = zipped.namelist()
        if len(names) != len(hashes) or len(names) != len(set(names)):
            raise RuntimeError('ZIP inventory verification failed')
        for name, value in hashes.items():
            target = release.name + '/' + name
            canonical = PurePosixPath(target)
            if canonical.is_absolute() or '..' in canonical.parts or '\\' in target:
                raise RuntimeError('Noncanonical ZIP path: ' + target)
            with zipped.open(target) as handle:
                actual = hashlib.file_digest(handle, 'sha256').hexdigest()
            if actual != value or digest(release / name) != value:
                raise RuntimeError('ZIP/release checksum mismatch: ' + name)
    os.replace(temp, archive)
    archive_hash = digest(archive)
    archive.with_suffix(archive.suffix + '.sha256').write_text(archive_hash + '  ' + archive.name + '\n', encoding='utf-8', newline='\n')
    return {'status': 'PASS', 'releaseFiles': len(hashes), 'zipBytes': archive.stat().st_size,
            'zipSha256': archive_hash, 'zipCrcVerified': True, 'everyZipFileSha256Matched': True,
            'canonicalUniqueZipPaths': True, 'runtimeWorkspaceExcluded': True}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('seal', 'verify-source', 'assemble', 'package'))
    parser.add_argument('--tree', type=Path)
    parser.add_argument('--baseline-immutable', type=Path)
    parser.add_argument('--baseline-app', type=Path)
    parser.add_argument('--carriers', type=Path)
    parser.add_argument('--release', type=Path)
    parser.add_argument('--tools', type=Path, default=Path(__file__).parent)
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--scratch', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    requirements = {'seal': ('tree',), 'verify-source': ('tree',),
                    'assemble': ('tree', 'baseline_app', 'carriers', 'release'),
                    'package': ('release', 'archive', 'scratch')}
    for name in requirements[args.mode]:
        if getattr(args, name) is None:
            parser.error('--' + name.replace('_', '-') + ' is required for ' + args.mode)
    if args.mode == 'seal':
        result = seal(args.tree, args.baseline_immutable)
    elif args.mode == 'verify-source':
        result = verify_source(args.tree)
    elif args.mode == 'assemble':
        result = assemble(args.tree, args.baseline_app, args.carriers, args.release, args.tools)
    else:
        result = package(args.release, args.archive, args.scratch)
    if args.report:
        write_json(args.report, result)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
