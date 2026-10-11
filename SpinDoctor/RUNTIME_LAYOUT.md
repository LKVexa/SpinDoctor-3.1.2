# SpinDoctor runtime folder (repository copy)

In the release package this folder is the runnable application: `BOOT.cmd`,
`harness/TIFFGifHoloHarness.cs`, the `cartridge/` carrier images and about
115 documentation, dashboard and launcher files.

To keep the repository lean, only the files that are unique to the runtime
folder are kept here:

- `BOOT.cmd` - the launcher
- `harness/TIFFGifHoloHarness.cs` - the generic C# harness BOOT compiles in-process
- `RUN_SPINDOCTOR.cmd`, `RUN_QUANTUM_XPU.cmd` - shortcuts that call BOOT.cmd
- `README-2ADIC-SHEET.txt`, `LICENSE_GPL3_RUSSELL_PHILIP_SMITHSON`

Every other file in the release's `SpinDoctor/` folder is byte-identical to the
file of the same name in `source/mvp/` (including `native/`), and
`OPEN_SIMULATOR.html` is identical to `source/mvp/SpinDoctor.html`.

The carriers (`cartridge/SpinDoctor-3.1.2.tiff` and `.gif`, about 33 MB) are
binary and are not in the repository. BOOT.cmd needs them, so download the
release package to run SpinDoctor.
