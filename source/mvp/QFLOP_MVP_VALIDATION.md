# QFLOP Virtual Raster MVP Validation

Release: **0.8.0**

## Static/reference generation

- Carrier anchor SHA-256: `b16a45e1b571d318763fb2402aa33d80bcb74398a138681ec599a810261e4910`
- AVK-1 checkpoint @ 1,000,000: `61a26c1d:55c9cdf2:08c0d2de`
- QCK-1: **500,000,000 FMA lanes = 1,000,000,000 FLOP-eq/epoch**
- Vector program: **122,070 × VFMA-4096 + 1 × VFMA-1280 = 122,071 commands/epoch**
- Represented compute @1 MHz: **1 QFLOP/s-eq**
- Physical proof slice: 1,000,000 lanes / 4,000,000 bytes; SHA-256 `05d921d7fa86cc5902c8309216c0b98fc98676831f6c01ec49bf2a13d65cb3db`
- Fixed compute range vectors: **6**
- Fixed raster coordinate vectors: **10**
- Virtual raster: **25B × 40B = 10^21 pixels**
- RGBA8 equivalent: **4 ZB**
- BigTIFF materialization shard: valid BigTIFF v43, 1024 × 1024 RGB tiled sample.

## Runtime gates

Runtime PASS is machine-specific. The dashboard requires measured sequential AVK-1 ≥1 MHz and final screen viewport materialization ≤2 seconds, in addition to all deterministic reference gates.

## Claim boundary

A PASS does not mean the host physically performs 1 QFLOP/s or stores/renders 4 ZB. It means the QCK-1 program and VBT-1 raster remain deterministically addressable/selectively reconstructable while the sequential carrier clock and physical viewport meet their measured gates.
