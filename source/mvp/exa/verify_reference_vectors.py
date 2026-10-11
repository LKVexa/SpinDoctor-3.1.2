from pathlib import Path
import json, hashlib
import tifffile
root=Path(__file__).resolve().parent
ref=json.loads((root/'reference_vectors.json').read_text())
prog=json.loads((root/'exaflop_program.json').read_text())
man=json.loads((root/'virtual_raster_cube_manifest.json').read_text())
base=(root.parent/'availability'/'base_page_frame1.bin').read_bytes()
checks=[]
def add(name,ok,detail=''):
    checks.append((name,bool(ok),detail))
    print(('PASS' if ok else 'FAIL'),name,detail)
add('carrier anchor',hashlib.sha256(base).hexdigest()==ref['carrierBasePageSha256'])
add('EXAFLOP envelope',
    prog['flopEquivalentPerEpoch']==1_000_000_000_000
    and prog['fmaLanesPerEpoch']==500_000_000_000
    and prog['fullVectorCommands']==122_070_312
    and prog['tailVectorWidth']==2048
    and prog['vectorCommandsPerEpoch']==122_070_313)
add('1 MHz represented compute',prog['representedFlopEquivalentPerSecondAt1MHz']==1_000_000_000_000_000_000)
add('raster cube dimensions',
    man['width']==25_000_000_000 and man['height']==40_000_000_000
    and man['depth']==1000 and man['voxelCount']=='1000000000000000000000000')
add('4 YB materialized equivalent',man['fullyMaterializedEquivalentBytes']=='4000000000000000000000000')
with tifffile.TiffFile(root/'sample_1000_depth_volume_shard.btf') as tf:
    add('sample shard is BigTIFF',tf.is_bigtiff)
    add('sample shard depth',len(tf.pages)==1000)
    add('sample shard dimensions',tf.series[0].shape==(1000,64,64,3))
print(f'{sum(x[1] for x in checks)}/{len(checks)} static gates passed')
raise SystemExit(0 if all(x[1] for x in checks) else 1)
