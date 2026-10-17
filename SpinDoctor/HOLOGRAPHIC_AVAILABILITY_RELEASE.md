# Lunar127 0.5.0 — Holographic Availability Lab

This release promotes the 1 MHz availability hypothesis into an executable research benchmark.

## Primary question

Can the compact processable-hologram representation execute at least **1,000,000 deterministic logical transitions per second** while making a `512 × 512 × 8-bit` state equivalent continuously reconstructable and verifiable without materializing all `262,144` bytes at every epoch?

## Added

- `HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html` — self-contained AVK-1 test and virtual console.
- `RUN_1MHZ_HOLOGRAPHIC_AVAILABILITY_TEST.cmd` — one-click auto-run.
- `RUN_FULL_RESEARCH_DEMO.cmd` — existing carrier/evaluator verification followed by the AVK-1 test.
- `availability/base_page_frame1.bin` — exact symbol lane from the actual first processable GIF data frame.
- `availability/reference_vectors.json` — precomputed carrier, transition-chain, selective-range, and full-checkpoint references.
- `availability/verify_reference_vectors.py` — independent Python reference implementation.
- `availability/AVAILABILITY_SPEC.md` — benchmark semantics, equations, pass criteria, and claim boundary.

## Headline baseline

At exactly 1 MHz, the attached page geometry represents:

- **262.144 GB/s** full-state equivalent availability.
- **261.888 GB/s** evolving-payload equivalent availability.

These are logical/reconstructable state rates, not physical I/O throughput or new-entropy rates.

## PASS requirements

The dashboard will only report overall PASS when the measured sequential transition rate is at least 1 MHz and every reached fixed reference proof agrees.
