# Lunar127 1.1.3 Full-Hologram Containment Audit

## Required boundary

The authoritative TIFF/GIF logical archive contains the complete 10Q@1M MVP **and the complete UltraWide Appliance**. No UltraWide repository, Python module, appliance file, documentation file, example, test, build script, word-chain asset, or source ZIP is loaded from outside the carrier.

## UltraWide containment

- Repository files embedded: **73**
- Repository bytes embedded: **1,522,808**
- Original source ZIP embedded: **348,857 bytes**
- Original ZIP SHA-256: `a2fb210325b75ba832de467040179ad651f4cf0058dfe484c5cc22d92af97b24`
- Resident BRWWORD: **131,072 bytes / 1,048,576 bits**
- Manifest: `mvp/ultrawide-appliance/ULTRAWIDE_APPLIANCE_MANIFEST.json`

TQK-1 verifies the source ZIP byte count/hash, every repository file byte count/hash, aggregate repository size, and exact declared file count before the timed sequential benchmark. The generic carrier harness separately verifies every image-resident entry through `integrity/immutable.json` before materialization.

## Bootstrap rule

`BOOT.cmd` and `harness/TIFFGifHoloHarness.cs` remain outside only because a host-side reader is required to decode a TIFF/GIF carrier. Exact copies are also stored at `mvp/bootstrap/`. The external copies are therefore not unique authoritative program content.

## Represented-compute boundary

The `10^25 FLOP-eq/epoch`, `10^31 FLOP-eq/s at 1 MHz`, and `10^40` virtual-voxel figures are procedural address spaces, not physically stored operation/voxel arrays.
