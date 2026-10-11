# Lunar127 17T@1M MVP — Validation Report

Release: **0.7.0**  
Schema: `LUNAR127/17T-AT-1M/1`

## Static target

- Hologram clock threshold: **≥ 1,000,000 sequential epochs/s**.
- VCK-2 compute envelope: **17,000,000 FLOP-equivalent operations/epoch**.
- FMA lanes: **8,500,000/epoch**.
- Addressable vector commands: **2,076/epoch**.
- Represented compute at 1 MHz: **17.000 TFLOP/s-equivalent**.
- Represented vector-command availability at 1 MHz: **2.076 Gcommands/s**.
- Represented state evolution at 1 MHz: **262.144 GB/s**.

## Independent reference generation

Reference vectors were generated in Python from `availability/base_page_frame1.bin`, whose SHA-256 is:

`b16a45e1b571d318763fb2402aa33d80bcb74398a138681ec599a810261e4910`

The compute model was then independently reimplemented in JavaScript/Node and compared byte-for-byte through SHA-256.

### Selective compute challenges

- epoch `472,118`, lanes `6,291,456..6,295,551`: **PASS** — `d708c7f2f7fd94c673265cdc1387b98daf39297c957cae4130493f3d5bc101f8`
- epoch `777,777`, lanes `131,072..135,167`: **PASS** — `220b80d1a4bdf93802d82726150be01daaa20adaf772e198c606a9296990e0f7`
- epoch `999,983`, lanes `8,000,000..8,004,095`: **PASS** — `00c760c3491ac951d49d6da24b75f586551f71c43f090938d56e949af47c986f`
- epoch `1,000,000`, lanes `0..4,095`: **PASS** — `8373606676f0e2554db43347f64d9b47c0694ee96162496b8f06ce67716fb539`

### Complete compute checkpoint

- Epoch: **1,000,000**
- Output lanes: **8,500,000**
- Materialized bytes: **34,000,000**
- Python reference SHA-256: `2f8ab208f6cd3c734876f9d17b2928029e25fa981205751eaa89b1dfff5232dc`
- Independent JavaScript/Node SHA-256: **MATCH / PASS**

### Hologram-state proof

- AVK-1 sequential chain at epoch 1,000,000: **MATCH / PASS**
- Complete 262,144-byte state SHA-256: `29bad48adbc1f314281d187aec587b9543252e992e7739bd3611df60310b2012` — **MATCH / PASS**

### Syntax / packaged verifier

- `moonshot/verify_reference_vectors.py`: carrier + all selective challenges **PASS**.
- Dashboard JavaScript: Node syntax check **PASS**.
- Embedded worker JavaScript: Node syntax check **PASS**.

The packaging environment's browser is policy-blocked from opening local `file://` and loopback test pages, so automated UI driving was not used as the conformance authority. The computational kernels and fixed proof vectors were instead cross-validated directly in independent Python and JavaScript implementations.

## Important measurement boundary

These checks validate deterministic availability semantics and fixed proof vectors. The **17 TFLOP/s-equivalent** figure is the represented compute envelope obtained from `17,000,000 FLOP-eq/epoch × 1,000,000 epochs/s`. It is not conventional measured hardware TFLOPS. On the user's machine the dashboard separately measures:

- actual sequential AVK-1 epoch rate;
- actual time to physically materialize all 8.5 million compute lanes for the full checkpoint;
- corresponding physical checkpoint GFLOP-equivalent/s.

Only **Full proof** mode can produce an overall moonshot PASS. Quick mode intentionally reports PARTIAL because it omits the 34 MB complete compute checkpoint.
