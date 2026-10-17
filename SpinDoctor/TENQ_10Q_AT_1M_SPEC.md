## Run-session profile (1.1.4)

Release 1.1.4 adds `LUNAR127/RUN-SESSION-BINDING/1`.

For randomized dashboard runs:

1. `launchSeed = SHA256(OS_RNG_256 || time || runCounter || GUID)`.
2. `sessionSeed = SHA256(launchSeed || carrierProgramBinding || BRWWORD_SHA256)`.
3. TQV-1 voxel salts are derived from the verified word digest and `sessionSeed`.
4. The dashboard creates a fresh 16-link session checkpoint chain and 64 lane + 64 voxel challenge addresses from the active session seed.
5. Fit, zoom, mode, slice and render operations are view-only and must not mutate launch/session seed.
6. Replay/custom seed bypasses step 1 and begins from the supplied 64-hex launch seed, making steps 2–4 deterministic.

Carrier program binding: `4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6`.

The carrier program binding is deliberately not the final TIFF/GIF file hash, because embedding the final container hash inside the same container would be self-referential. The outer carrier is separately stream-hash-verified before the image-resident payload is materialized.

---


## Full UltraWide Appliance containment (1.1.3)

The TIFF/GIF logical archive now contains the **complete UltraWide Appliance**, not only the resident 1,048,576-bit BRWWORD. It embeds both the exact repository tree and the original source ZIP. A carrier-resident SHA-256 manifest covers every appliance file; TQK-1 validates the exact file set, total bytes, source ZIP byte count, and source ZIP digest before any timed epoch benchmark begins. External `BOOT.cmd` and generic harness source are launch-only copies and are also duplicated inside the carrier for provenance.

# Lunar127 10Q@1M UltraWide represented-compute specification

Release: **1.1.3**  
Profiles: **TQK-1** sequential kernel, **TQL-1** queryable compute domain, **TQV-1** virtual raster cube

## Claim boundary

The release target is **10 quettaFLOP/s-equivalent represented compute**, not 10 quettaFLOP/s of physically executed laptop arithmetic.

At the release gate:

- sequential hologram clock: `1,000,000 epochs/s`;
- deterministic compute domain per epoch: `10^25 FLOP-eq`;
- represented rate: `10^31 FLOP-eq/s = 10 quettaFLOP/s-eq`.

Every TQL-1 lane is one exact-domain multiply followed by one add and is counted as two FLOP-equivalent operations. Therefore the admitted lane address space is **5 × 10^24 lanes per epoch**.

The dashboard must report the measured host transition rate, a separate physical scalar floating-point diagnostic, and algorithmic state-byte accounting. No claim is made that represented work was physically materialized in advance.

## Slow/control path

At launch:

1. decode and verify the TIFF-GIF-HOLO/4 carrier;
2. materialize only the verified `mvp/` projection to a temporary directory;
3. validate the exact 131,072-byte `BRWWORD/1` UltraWide word;
4. verify its image/source/BRIR hashes, zero padding and one-word manifest identity;
5. hash `TENQ_MACROPROGRAM_CORE.json`;
6. reconstruct the canonical LCTL-BRIM/1 source expected from that digest;
7. require exact equality with the source stored inside the resident word;
8. execute fixed 1,000,000-epoch TQK-1 reference-vector validation;
9. only then enter the measured sequential benchmark.

The word is read and verified once. It is not serialized, hashed, interpreted by Python, or copied through a 1,048,576-bit arithmetic path every microsecond.

## TQK-1 hot state

The hot state is exactly four wrapping unsigned 64-bit limbs: 256 bits total. Initial constants are derived from the SHA-256 digest of the resident word. Every epoch is strictly data-dependent on the previous state and mutates all four limbs with wrapping add, XOR, rotate and multiply operations.

The release benchmark uses three measured rounds and reports the median dependent epoch rate. A release PASS requires at least **1,000,000 sequential epochs/s**.

## Checkpoint path

Every 65,536 epochs, and at the final epoch, a SHA-256 checkpoint commits:

`previous checkpoint || resident-word SHA-256 || epoch-u64le || 256-bit state`

The hash path is periodic and deliberately outside the per-epoch transition. Fixed 1,000,000-epoch state and checkpoint roots are included in the image-resident reference-vector bank.

## TQL-1 queryable compute domain

Each admitted address is `(epoch, lane)` with:

`0 <= lane < 5 × 10^24`.

The lane address is split into three 32-bit limbs. Together with the epoch and resident-word digest, 32-bit mixing derives integer operands `a`, `b`, `c` in `[-1024, 1023]`.

The lane result is:

`result = a * b + c`

The range is selected so that product-plus-add is exactly representable in FP32; this avoids cross-language floating-point ambiguity while preserving two FLOP-equivalent arithmetic operations per lane.

After the measured run, the C# kernel draws 64 unpredictable lane addresses with the host cryptographic RNG and materializes only those answers. The dashboard independently reconstructs each result in JavaScript. A mismatch fails the release gate.

## TQV-1 virtual raster cube

Geometry:

- X: `25,000,000,000,000` voxels;
- Y: `40,000,000,000,000` voxels;
- Z: `10,000,000,000,000` voxels;
- total: `10^40` addressable voxels.

A flat RGBA8 materialization would require `4 × 10^40` bytes. It is never allocated.

TQV-1 maps `(X,Y,Z,epoch,resident-word digest)` to a deterministic RGBA8 value. After the benchmark, 64 unpredictable 3-D voxel challenges are answered by C# and independently recomputed by the browser evaluator.

The visual console maps screen rays into virtual coordinates and samples a finite number of Z positions. Zooming changes the queried coordinate window; it does not expand the cube into memory. Initial viewport fill must complete within 2 seconds.

## Release gates

A 10Q@1M PASS requires all of the following simultaneously:

1. resident `BRWWORD/1` structural/integrity verification PASS;
2. fixed 1,000,000-epoch TQK-1 state/checkpoint vector PASS;
3. measured median sequential clock >= 1,000,000 epochs/s;
4. `10^25` FLOP-eq domain per epoch;
5. `10^31` represented FLOP-eq/s at the 1 MHz gate;
6. 1,048,576-bit UltraWide program resident and verified;
7. 256-bit hot state;
8. 64 randomized compute-lane challenges PASS in the independent JavaScript evaluator;
9. 64 randomized 3-D voxel challenges PASS in the independent JavaScript evaluator;
10. checkpoint chain present and internally valid;
11. `25T × 40T × 10T = 10^40` virtual cube geometry exact;
12. viewport first-fill <= 2 seconds;
13. physical scalar FLOP diagnostic reported separately;
14. algorithmic byte accounting reported separately and not mislabeled as measured DRAM traffic;
15. represented/materialized diagnostic amplification reported explicitly.

## Trust boundary

SHA-256 hashes provide integrity binding, not author authentication. The UltraWide artifact is an unsigned preview. The external C# TIFF/GIF harness and `BOOT.cmd` are platform bootstrap components; the TQK-1 source, UltraWide word, descriptor, references, dashboard, proof rules and launcher are carried inside the processable TIFF/GIF image.


## 1.1.3 randomized-raster session semantics

Each launch creates a fresh 256-bit seed from the host OS cryptographic random-number generator unless `LUNAR127_RASTER_SEED` supplies an exact 64-hex-character replay seed. The verified BRWWORD hash and launch seed are mixed into four session voxel salts. Therefore the 25T × 40T × 10T procedural raster is different between ordinary launches but deterministic within a run. Random post-run voxel challenges and the browser renderer use the same seed-bound evaluator. The measured checkpoint chain also commits to the launch seed; the fixed preflight vector remains seed-independent so the sequential kernel baseline stays reproducible.
