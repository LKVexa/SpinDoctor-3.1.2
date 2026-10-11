# Lunar127 0.3.1 TIFF-GIF-HOLO — Hotfix 3 validation

Hotfix 3 addresses the Windows `CS0160` compiler failure reported from Hotfix 2.

## Source checks

- `public_RuntimeEngine_Dispose`: **PASS**
- `specific_catch_before_supertype`: **PASS**
- `no_old_invalid_then_disposed_sequence`: **PASS**
- `persistent_boot_pause`: **PASS**
- `build_log_capture`: **PASS**
- `gif_exists`: **PASS**
- `gif_sha256`: `96d813f3b316bfa91a96b12dc7c42ccf59cbfe0e8015d3b808add8aeef01f87d`
- `tiff_exists`: **PASS**
- `tiff_sha256`: `af3cf16feb21e8cc0315945cca0b646254d4d5e0df60f85c6bcd88da020839c8`

## Scope

The C# source was statically checked in this Linux build environment, but the Windows .NET Framework compiler is not available here. Therefore native Windows compilation remains an acceptance test to run on the target machine. The TIFF/GIF carrier bytes were not altered by this hotfix.
