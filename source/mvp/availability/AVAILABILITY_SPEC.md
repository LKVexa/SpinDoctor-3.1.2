# Lunar127 AVK-1 — Holographic Availability Benchmark Specification

Release: **0.5.0**  
Schema: `LUNAR127/HOLOGRAPHIC-AVAILABILITY/1`

## Central research hypothesis

A compact TIFF-GIF processable hologram can sustain a deterministic logical transition frequency of at least **1 MHz** while maintaining a full-state equivalent of hundreds of GB/s, provided the state is reconstructable, addressable, and verifiable on demand without complete materialization at every epoch.

## Carrier anchor

The benchmark is anchored to the **actual first processable data page** (frame 1) of:

`cartridge/Lunar127_Showcase_0.3.1_PROCESSABLE.gif`

The benchmark extracts the red/symbol lane used by the TIFF-GIF-HOLO/4 carrier:

- Geometry: `512 × 512`
- Symbol width: `8 bits`
- Full page: `262,144 bytes`
- Stable control header: `256 bytes`
- Evolving payload: `261,888 bytes`
- Base page SHA-256: `b16a45e1b571d318763fb2402aa33d80bcb74398a138681ec599a810261e4910`

The benchmark page is included as `availability/base_page_frame1.bin` so the reference vectors can be independently regenerated.

## AVK-1 execution model

Each epoch performs two related operations:

1. **Sequential transition proof.** A three-word state `(A,B,C)` is updated from the prior epoch and the current epoch key. Because each state depends on the prior state, reaching epoch `N` requires executing the sequence of transitions; the benchmark cannot obtain the final chain simply by incrementing a display counter.
2. **Direct state availability.** Any symbol of the 512×512 virtual state can be reconstructed directly from the anchored base page, epoch number, and symbol index. The full history does not need to be stored or materialized.

The first 256 control-header bytes remain stable. The 261,888 payload bytes are deterministically phase/texture transformed by the AVK-1 integer kernel.

## 1 MHz interpretation

At exactly `1,000,000 epochs/s`:

- Full-state equivalent availability = `262,144 × 1,000,000 = 262.144 GB/s`.
- Evolving payload availability = `261,888 × 1,000,000 = 261.888 GB/s`.

These are **represented/reconstructable state rates**. They are not claims of physical RAM, storage, display, optical, or network throughput and are not claims of equal new information entropy.

## Required proofs

A run passes the primary hypothesis only when:

- measured sequential AVK-1 transition rate is `≥ 1,000,000 epochs/s`;
- the real carrier page SHA-256 matches the fixed anchor;
- every reached sequential-chain checkpoint matches a predetermined reference state;
- every reached 4 KiB selective reconstruction challenge matches its predetermined SHA-256;
- every reached 262,144-byte full-state checkpoint matches its predetermined SHA-256.

The reference vectors were generated before the browser run and live in `reference_vectors.json`.

## Availability expansion

The dashboard reports:

`availability expansion = represented full-state bytes / explicitly materialized challenge + checkpoint bytes`

This is an experimental **availability/materialization ratio** for the fixed challenge schedule. It is not a compression ratio and it does not estimate physical memory-bus traffic.

## Independent verification

With Python 3:

`python availability\verify_reference_vectors.py`

For all 10 million reference-chain epochs:

`python availability\verify_reference_vectors.py --deep`
