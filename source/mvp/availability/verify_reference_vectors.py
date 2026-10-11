#!/usr/bin/env python3
"""
Independent reference-vector verifier for Lunar127 AVK-1.
Uses only Python stdlib and availability/base_page_frame1.bin.
"""
from pathlib import Path
import argparse, hashlib, json, sys

ROOT = Path(__file__).resolve().parent
SPEC = json.loads((ROOT / "reference_vectors.json").read_text(encoding="utf-8"))
BASE = (ROOT / "base_page_frame1.bin").read_bytes()
MASK = 0xffffffff
SEED0 = int(SPEC["kernel"]["seed0"])
SEED1 = int(SPEC["kernel"]["seed1"])

def mix32(x):
    x &= MASK
    x ^= x >> 16
    x = (x * 0x7feb352d) & MASK
    x ^= x >> 15
    x = (x * 0x846ca68b) & MASK
    x ^= x >> 16
    return x & MASK

def epoch_key(epoch):
    return mix32(SEED0 ^ ((epoch * 0x9e3779b1) & MASK) ^ SEED1)

def fill_range(epoch, offset, length):
    k = epoch_key(epoch)
    out = bytearray(length)
    for j in range(length):
        i = offset + j
        b = BASE[i]
        if i < 256:
            out[j] = b
            continue
        x = mix32(k ^ ((i * 0x85ebca6b) & MASK) ^ ((b << 24) & MASK) ^ 0x27d4eb2d)
        out[j] = (b ^ x ^ (x >> 8) ^ (x >> 16) ^ (x >> 24)) & 0xff
    return bytes(out)

def materialize(epoch):
    return fill_range(epoch, 0, len(BASE))

def verify_chain(limit):
    refs = {int(k): v for k, v in SPEC["reference"]["chainStates"].items() if int(k) <= limit}
    a = mix32(SEED0 ^ 0xa5a5a5a5)
    b = mix32(SEED1 ^ 0x5a5a5a5a)
    c = mix32(SEED0 ^ SEED1 ^ 0xc3efe9db)
    ok = True
    for epoch in range(1, limit + 1):
        k = epoch_key(epoch)
        a = mix32(a ^ k ^ epoch)
        b = (((b ^ a) * 1664525) + 1013904223) & MASK
        c = mix32((c + b) ^ ((epoch * 0x27d4eb2d) & MASK))
        if epoch in refs:
            r = refs[epoch]
            match = (a, b, c) == (int(r["a"]), int(r["b"]), int(r["c"]))
            print(("PASS" if match else "FAIL"), "chain", epoch, (a,b,c))
            ok &= match
    return ok

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--deep", action="store_true", help="verify chain through 10,000,000 epochs instead of 1,000,000")
    args = ap.parse_args()

    ok = True
    h = hashlib.sha256(BASE).hexdigest()
    anchor_ok = h == SPEC["carrier"]["basePageSha256"]
    print(("PASS" if anchor_ok else "FAIL"), "carrier-anchor", h)
    ok &= anchor_ok

    for q in SPEC["reference"]["rangeChallenges"]:
        if not args.deep and int(q["epoch"]) > 1_000_000:
            continue
        got = hashlib.sha256(fill_range(int(q["epoch"]), int(q["offset"]), int(q["length"]))).hexdigest()
        match = got == q["sha256"]
        print(("PASS" if match else "FAIL"), "range", q["epoch"], q["offset"], got)
        ok &= match

    for ep_s, expected in SPEC["reference"]["fullCheckpointSha256"].items():
        ep = int(ep_s)
        if not args.deep and ep > 1_000_000:
            continue
        got = hashlib.sha256(materialize(ep)).hexdigest()
        match = got == expected
        print(("PASS" if match else "FAIL"), "full", ep, got)
        ok &= match

    ok &= verify_chain(10_000_000 if args.deep else 1_000_000)
    print("OVERALL", "PASS" if ok else "FAIL")
    return 0 if ok else 2

if __name__ == "__main__":
    raise SystemExit(main())
