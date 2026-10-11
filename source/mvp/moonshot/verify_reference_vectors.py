from pathlib import Path
import argparse, hashlib, json, struct
ROOT=Path(__file__).resolve().parent.parent
REF=json.loads((ROOT/'moonshot'/'reference_vectors.json').read_text())
BASE=(ROOT/'availability'/'base_page_frame1.bin').read_bytes()
HEADER=256; PAYLOAD=261888; LANES=8_500_000

def f32(x): return struct.unpack('<f',struct.pack('<f',x))[0]
def lane(epoch,g):
    ia=HEADER+((g+epoch*17+13)%PAYLOAD); ib=HEADER+((g*3+epoch*31+29)%PAYLOAD); ic=HEADER+((g*7+epoch*43+47)%PAYLOAD)
    va=BASE[ia]^((epoch+g)&255); vb=BASE[ib]^(((epoch>>8)+g*3)&255); vc=BASE[ic]^(((epoch>>16)+g*7)&255)
    a=f32((va-128)/64.0); b=f32((vb-128)/64.0); c=f32((vc-128)/128.0)
    return f32(f32(a*b)+c)
def hash_range(epoch,start,count):
    h=hashlib.sha256()
    for g in range(start,start+count): h.update(struct.pack('<f',lane(epoch,g)))
    return h.hexdigest()
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--full',action='store_true'); a=ap.parse_args()
    ok=hashlib.sha256(BASE).hexdigest()==REF['carrier']['basePageSha256']; print('carrier', 'PASS' if ok else 'FAIL')
    for q in REF['reference']['computeRangeChallenges']:
        got=hash_range(q['epoch'],q['laneStart'],q['laneCount']); p=got==q['sha256']; ok&=p; print('range',q['epoch'],q['laneStart'],'PASS' if p else 'FAIL',got)
    if a.full:
        h=hashlib.sha256(); cp=REF['reference']['fullCompute'];
        for g in range(LANES): h.update(struct.pack('<f',lane(cp['epoch'],g)))
        got=h.hexdigest(); p=got==cp['sha256']; ok&=p; print('full-compute','PASS' if p else 'FAIL',got)
    print('OVERALL','PASS' if ok else 'FAIL')
    raise SystemExit(0 if ok else 1)
if __name__=='__main__': main()
