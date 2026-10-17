# Complete package restoration — 2026-10-16

## Diagnosed startup failure

The previous default branch at `7a0ad64a85f6c2cddedb0f22505d06e020c96355` contained the launcher and source code but excluded `SpinDoctor/cartridge/`, TIFF/GIF carriers and other package data through `.gitignore`. Running `SpinDoctor\BOOT.cmd verify` reproduced exit code 2 with `Requested tiff carrier is missing.` The repository had no GitHub releases from which to download the omitted package. Earlier source-build checks did not establish that a fresh GitHub download could launch.

## Restored files

All 615 files from the supplied original `SpinDoctor-3.1.2.zip` are restored byte-for-byte, including both carrier images, source, PDF hand guide, examples, sample reports and bundled notices. All 614 entries in the original `SHA256SUMS.txt` match. ZIP SHA-256: `d4045efc6660352c367e4ec198692e9b0fa89a34599d80225bf11ed242ae8cd2`.

The attachment is the original 29-qubit 3.1.2 release. The separate 298-qubit update and its version-specific examples/results were removed from the restored tree to keep source and sealed carriers consistent. The previous tree, including its tests and documentation, is preserved on [before-replacement-20261016](https://github.com/LKVexa/SpinDoctor-3.1.2/tree/before-replacement-20261016) and in commit history. Repository licensing and the separately uploaded command-language PDF are retained.

Only repository-level documentation, `.gitignore`, `.gitattributes`, and `VERIFY_PACKAGE.ps1` were added or changed outside the original package. Package files and sealed carriers were not patched. `.gitattributes` disables text conversion so a clone/download preserves the manifest bytes.

## Checks performed on the supplied package

Windows PowerShell 5.1, fresh isolated scratch folders:

| Check | Result |
|---|---|
| Original file checksums | 614/614 match |
| Repository verification helper and actual BOOT verify | Pass; BOOT exits 0 |
| TIFF and GIF verification and smoke | Both pass; 292 entries, matching payload streams |
| TIFF materialization | 291 immutable files match; repeated materialization preserves synthetic session/report files |
| Native engine compilation and regressions | 13 groups, 38,766 assertions pass |
| 3.1.2 folder regressions | 16 assertions pass, including a 128-command file |
| Browser script checks | 34 scripts compile; 7 checks pass |
| Native HTTP and CSV/report regressions | 59 checks pass through production request parsing/routes |
| Interface regressions | 70 checks pass with a version-only expectation correction in a temporary test copy |

The original interface test still expects `Version 3.1.1-candidate` at `validation/release-tools/tests/ui_regressions.js:72`. The delivered page says `Version 3.1.2`. The unmodified test passes its first 41 checks and then fails that stale assertion. Only the scratch test expectation was adjusted; the shipped package remains unchanged.

The original `PACKAGE_TOOLS.ps1` defaults to the older `Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE` carrier basename. Supply an absolute versioned carrier path as `-Target`, or use `VERIFY_PACKAGE.ps1` / `BOOT.cmd verify` as described in README.

These checks do not certify real OpenCL GPU execution or the graphical browser startup on every Windows machine. CPU engine and carrier/materialization checks were exercised; a compatible GPU driver remains necessary for GPU verification.
