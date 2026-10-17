# Lunar127 0.3.1 TIFF-GIF Validation

## Corrective objective

The 0.3.0 carrier looked like a static TIFF. Version 0.3.1 changes the carrier model itself: the processable hologram is now an ordered temporal frame stream with both standard multipage-TIFF and animated-GIF encodings.

## Seed-cartridge results

Independent Python decoding verified both carriers from their image pixels:

| Control | TIFF | GIF |
|---|---:|---:|
| Standard image frames | 9 | 9 |
| Processable data frames | 8 | 8 |
| Image-resident archive entries | 31 | 31 |
| Image-resident archive bytes | 1,982,196 | 1,982,196 |
| Image-resident VM bytecode | 17,440 B | 17,440 B |
| Same logical archive | PASS | PASS |
| Reversible temporal phase texture | PASS | PASS |
| Per-frame payload hashes | PASS | PASS |
| Ordered previous-frame chain | PASS | PASS |
| Complete archive hash | PASS | PASS |
| Image-resident immutable-bank map | PASS | PASS |

Seed SHA-256 values used for this package:

- TIFF: `af3cf16feb21e8cc0315945cca0b646254d4d5e0df60f85c6bcd88da020839c8`
- GIF: `96d813f3b316bfa91a96b12dc7c42ccf59cbfe0e8015d3b808add8aeef01f87d`

## Processable transaction test

For **each** standard container independently, the reference evaluator performed a complete image transaction:

1. Decode image frames.
2. Reverse phase texture.
3. Verify page hashes and temporal page chain.
4. Reassemble the archive.
5. Verify immutable image-resident entries.
6. Execute the image-resident VM for one quantum.
7. Change machine state and journal.
8. Repack the archive.
9. Generate a new phase-textured frame stream.
10. Encode a new complete TIFF or GIF.
11. Reopen it from disk.
12. Revalidate it.
13. Confirm restored machine state is byte-for-byte equivalent to the committed state.

Measured reference transaction results:

- TIFF transaction 1: **75 VM instructions**, committed as tick **1**, encode+validation approximately **1.58 s** in the Python reference implementation.
- GIF transaction 1: **75 VM instructions**, committed as tick **1**, encode+validation approximately **1.87 s** in the Python reference implementation.
- Both committed images reloaded with exact modeled state.
- Both image-resident VM states then continued deterministically to touchdown in **9 additional VM quanta** at the validation warp setting.
- Both reached the same final modeled state: vertical velocity **-1.5 m/s**, horizontal velocity **0.24 m/s**, fuel fraction **0.215**.
- A deliberate mutation of a used encoded data symbol was rejected in both formats by the embedded payload hash.

The machine-readable results are in `REFERENCE_VALIDATION.json`.

## Windows acceptance status

The C# Windows harness source has been updated to read, write, re-read and atomically commit either the temporal TIFF or temporal GIF carrier, including the reversible phase texture and frame-chain checks. This Linux build environment does not contain the Windows .NET Framework compiler/runtime, so the native Windows GUI itself has **not** been executed here. The reference evaluator validates the carrier architecture and image-resident VM path; `BOOT.cmd` is the remaining Windows acceptance gate.
