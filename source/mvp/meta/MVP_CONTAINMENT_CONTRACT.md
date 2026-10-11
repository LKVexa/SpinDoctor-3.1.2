# Lunar127 Hologram-Contained MVP Contract

Release: **1.1.3**

The authoritative MVP filesystem is stored inside the TIFF-GIF-HOLO/4 carrier.

## Distributed bootstrap allowlist

Only these implementation pieces are permitted outside the processable hologram:

1. `BOOT.cmd` — compile/verify/mount/launch bootstrap.
2. `harness/TIFFGifHoloHarness.cs` — generic C# TIFF/GIF carrier decoder, integrity verifier, and materializer.

The `.tiff` / `.gif` files in `cartridge/` are the processable hologram itself. The TQK-1 kernel source, **complete UltraWide Appliance repository**, **original UltraWide source ZIP**, 1,048,576-bit UltraWide word, descriptor, proof vectors, challenge rules, checkpoint semantics, raster-cube evaluator, dashboard, specifications and launch script all live under `mvp/`. Copies of the bootstrap and harness are also embedded at `mvp/bootstrap/`.

The harness must verify the archive stream and immutable bank before materializing any runtime entry. The materialized directory is an ephemeral projection; the carrier remains authoritative. Runtime-generated benchmark metrics modify only that temporary projection, not the carrier.

The represented 10Q@1M metric is explicitly separate from physical host FLOPS.
