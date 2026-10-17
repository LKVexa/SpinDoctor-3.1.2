# TIFF-GIF-HOLO/4 Temporal Carrier

The processable hologram is an ordered image-memory stream, not a still image with a payload appended to it.

Each processable data frame is 512×512 pixels and carries one 8-bit symbol per pixel. The first 256 symbols are an unmasked control header. The remainder of the frame is phase-textured image memory.

The header carries the format/version, frame index, total processable-frame count, logical archive length, byte offset, payload length, complete archive SHA-256, payload SHA-256, cartridge identifier, committed VM tick, previous-page hash and codec identifier.

The payload and unused tail are transformed with a reversible integer triangular-wave phase texture. The mask depends on pixel coordinates, processable-page index and committed VM tick. This makes the temporal spool part of the image representation while preserving exact recoverability. The decoder reverses the mask before verifying the payload hash.

The ordered previous-page hash means reordering a GIF frame or TIFF page is invalid even if every individual page remains otherwise intact.

TIFF pages and GIF frames carry the same logical archive. The active container is rewritten after each committed VM quantum. A normal GIF viewer therefore sees an actual animated temporal carrier; the evaluator sees the same frames as processable memory.
