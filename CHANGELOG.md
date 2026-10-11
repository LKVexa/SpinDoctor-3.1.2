## 3.1.2 update 3.1.2-298-20261010

Native 298-qubit registers, exact global indices, MCX7, 59-qubit tensor factors, prepared joint-factor Z sampling, clearer circuit operand errors, and 298-aware query/folder preflight. Both carrier formats and launcher verification are updated. See UPDATE-298.txt for activation and execution scope.

# Changelog

## 3.1.2 (2026-10-10)
Parent: 3.1.1-candidate. Full audit: `AUDIT_3.1.2.md`.

### Fixed
- `q.folder`: unsupported files are reported in `skippedFiles` / `skippedFileCount` instead of being skipped silently. When a folder has no supported files, the error names one skipped file.
- `q.folder`: `.qasm2` and `.circuit` inputs are now accepted, since the parser already supported them.
- Text inputs: empty files and prose-only files get specific, accurate error messages.
- `q.folder status` / `q.sequence status`: archived polling reports are compact (about 4 KB instead of up to about 1 MB). The live response is unchanged.
- `q.folder help`: now lists supported extensions, limits, skipped-file reporting and the first-failure-stops rule.
- Test suite: the stale receipt assertion in `native_verification_preview_regressions` was aligned with the 3.1.x receipt policy.

### Added
- `validation/release-tools/tests/v312_regressions.ps1` (16 assertions).
- `HANDGUIDE.md` / `HANDGUIDE.pdf`: a plain-language user guide. Every example command was verified on this build.

### Changed
- Version 3.1.2 everywhere (runtime, metadata, UI, carriers, BOOT.cmd, docs, release tooling).

### Unchanged / deferred
- qc-198 `q.compile synthesize` is still unavailable by design.
- `.md` is still not an input format.
- Per-command sequence report size is unchanged (see AUDIT finding H).
- All 241 presets give the same outcomes as in 3.1.1.

## 3.1.1-candidate and earlier
See `SpinDoctor/SpinDoctor-3.1.1-Release-Notes.txt` and the earlier release notes in `source/mvp/`.
