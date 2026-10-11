from pathlib import Path
import json, hashlib, struct
import tifffile
root=Path(__file__).resolve().parent
ref=json.loads((root/'reference_vectors.json').read_text())
prog=json.loads((root/'qflop_program.json').read_text())
man=json.loads((root/'virtual_raster_manifest.json').read_text())
base=(root.parent/'availability'/'base_page_frame1.bin').read_bytes()
checks=[]
def add(name,ok,detail=''):
    checks.append((name,bool(ok),detail)); print(('PASS' if ok else 'FAIL'),name,detail)
add('carrier anchor',hashlib.sha256(base).hexdigest()==ref['carrierBasePageSha256'])
add('QFLOP envelope',prog['flopEquivalentPerEpoch']==1_000_000_000 and prog['fmaLanesPerEpoch']==500_000_000 and prog['fullVectorCommands']==122070 and prog['tailVectorWidth']==1280)
add('1 MHz represented compute',prog['representedFlopEquivalentPerSecondAt1MHz']==1_000_000_000_000_000)
add('virtual raster dimensions',man['width']==25_000_000_000 and man['height']==40_000_000_000 and man['pixelCount']=='1000000000000000000000')
with tifffile.TiffFile(root/'sample_materialized_viewport.btf') as tf:
    add('sample shard is BigTIFF',tf.is_bigtiff)
    add('sample shard dimensions',tf.pages[0].shape==(1024,1024,3))
print(f'{sum(x[1] for x in checks)}/{len(checks)} static gates passed')
raise SystemExit(0 if all(x[1] for x in checks) else 1)
