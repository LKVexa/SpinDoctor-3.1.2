# Lunar127 Showcase 0.3.1 TIFF-GIF-HOLO — Hotfix 1

This hotfix corrects the Windows C# harness compile failure reported as CS0737.

## Fix

`RuntimeEngine` implements `System.IDisposable`, therefore `Dispose()` must be public. The harness now declares:

```csharp
public void Dispose()
```

instead of `internal void Dispose()`.

The build script also removes stale evaluator binaries before compiling, and `BOOT.cmd` now returns a nonzero status reliably if harness compilation fails.

The TIFF/GIF cartridge payload and image-resident Lunar127 machine are unchanged by this host-side hotfix.
