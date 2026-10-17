# Lunar127 0.8.0 — QFLOP@1MHz Virtual Raster MVP

This release extends the 17T@1M research build with **QCK-1** and **VBT-1**.

## QCK-1

- 1,000,000,000 FLOP-equivalent operations per hologram epoch.
- 1 MHz target clock.
- 1,000,000,000,000,000 FLOP-eq/s = **1 QFLOP/s-equivalent represented compute**.
- 500,000,000 deterministic FMA lanes per epoch.
- 122,071 addressable vector commands per epoch.

## VBT-1

- virtual raster: 25,000,000,000 × 40,000,000,000 = 10^21 pixels;
- RGBA8 full-materialization equivalent: 4 ZB;
- screen-sized LOD materialization with pan/zoom;
- ≤2 s viewport population gate;
- real BigTIFF sample shard included at `qflop/sample_materialized_viewport.btf`.

The QFLOP and 4-ZB figures are represented/addressable quantities. The dashboard reports physically executed slice throughput and viewport rendering time separately.
