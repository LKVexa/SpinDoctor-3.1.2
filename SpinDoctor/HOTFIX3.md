# Lunar127 0.3.1 TIFF-GIF-HOLO — Hotfix 3

## Windows compile failure fixed

Observed Windows compiler error:

`CS0160: A previous catch clause already catches all exceptions of this or of a super type ('System.InvalidOperationException')`

The failing code caught `InvalidOperationException` before `ObjectDisposedException`. In .NET, `ObjectDisposedException` derives from `InvalidOperationException`, making the later catch unreachable.

Hotfix 3 changes `SimulationForm.SafeBegin` to catch the specific exception first:

```csharp
catch (ObjectDisposedException) { }
catch (InvalidOperationException ex) { Diagnostics.Log(ex, "BEGININVOKE " + context); }
```

This preserves the intended behavior: normal disposal races are ignored, while other `InvalidOperationException` failures are logged.

## Additional hardening

- `BUILD_HARNESS.cmd` now records complete compiler output in `workspace\logs\build.log`.
- Compiler failures are echoed in the persistent command window before it pauses.
- Hotfix 2 crash-capture behavior is retained.
- The TIFF and GIF processable hologram carriers are unchanged.

## Run

Use `BOOT.cmd reset` for the GIF carrier, `BOOT.cmd tiff reset` for TIFF, or run `DIAGNOSE.cmd` first.
