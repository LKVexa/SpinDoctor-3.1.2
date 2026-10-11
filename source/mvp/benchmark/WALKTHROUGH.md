# Lunar127 TIFF-GIF Performance Walkthrough

Release: **0.4.0 instrumentation overlay**  
Image-resident cartridge: **Lunar127 Showcase 0.3.1 Hotfix 4**  
Carrier format: **TIFF-GIF-HOLO/4**

## Purpose

This dashboard separates two classes of metrics:

1. **Measured evaluator metrics** — timings observed by the supplied Windows/.NET TIFF-GIF evaluator or by the reference validation bundled with the simulation.
2. **Modeled spool metrics** — deterministic arithmetic from the actual attached carrier geometry at a selected page frequency. These are processing/transport requirements and capacity figures, **not proof that the current host, storage, display, or optical path can sustain that frequency**.

## Actual attached carrier geometry

- Processable page: 512 × 512 = 262,144 8-bit symbols.
- Unmasked control header: 256 bytes.
- Maximum phase-textured payload: 261,888 bytes/page.
- Current logical image-resident archive: 1,982,196 bytes.
- Current archive page count: 8 processable pages.
- GIF encoded container: 2,678,940 bytes, 9 total frames including cover.
- TIFF encoded container: 2,825,984 bytes, 9 total pages including cover.

## 1 MHz baseline

The dashboard defines 1 MHz as **1,000,000 processable data pages per second**.

At 1 MHz:

- Raw symbol lane: **262.144 GB/s** (2.097152 Tb/s).
- Maximum payload lane: **261.888 GB/s** (2.095104 Tb/s).
- Current Lunar127 archive passes: **125,000/s**.
- Current archive latency: **8.000 µs**.
- Effective current-archive data rate: **247.774500 GB/s** (1.982196 Tb/s).
- Phase-texture XOR demand at full payload: **261.888 billion byte transforms/s**.
- Page payload SHA-256 input demand: **261.888 GB/s**.
- Page-chain/header validation demand: **1,000,000 pages/s**.

## Walkthrough

Use **Start walkthrough** in the dashboard. It advances through:
`1 MHz → 2 MHz → 5 MHz → 10 MHz → 25 MHz → 50 MHz → 100 MHz → 250 MHz → 500 MHz → 1 GHz`

The 1 GHz endpoint is only the dashboard's model ceiling. It is intentionally not labeled a demonstrated hardware ceiling.

## Machine-local benchmark

Run:

`BENCHMARK_DASHBOARD.cmd`

The command rebuilds the supplied C# evaluator, runs its own `--verify` and `--smoke` paths against both carriers, writes `benchmark/latest_metrics.json` and `benchmark/latest_metrics.js`, then opens the dashboard.

The live benchmark includes executable startup overhead and therefore acts as a conservative end-to-end host measurement. It does not convert the Windows evaluator into a MHz-rate optical processor.

## Interpretation rule

A modeled spool frequency is feasible only when every real implementation stage can sustain the required page rate, byte rate, phase-texture transforms, hashing, memory movement, decode/encode, VM work, storage I/O, and commit semantics. The dashboard highlights the gap between the requested modeled rate and the currently measured host evaluator rate rather than hiding it.
