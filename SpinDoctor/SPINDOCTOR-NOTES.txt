SpinDoctor 3.1.2
See SpinDoctor-3.1.2-Release-Notes.txt for the 3.1.2 audit fixes: folder runs list skipped files, accept .qasm2/.circuit, explain empty or prose .txt inputs, and keep q.folder/q.sequence status polling reports compact.
See HANDGUIDE.md (or HANDGUIDE.pdf in the release folder) for a plain-language user guide.
See SpinDoctor-3.1.1-Release-Notes.txt for CSV report companions, measurement fitting and CSV gate-list simulation.
See SpinDoctor-3.1.1-CSV-Input-Guide.txt for selecting measurement columns, fitting equations and importing CSV circuits.
Earlier version notes below are preserved as history.

SpinDoctor 3.1.0 candidate
See SpinDoctor-3.1.0-Release-Notes.txt for circuit definition and report accounting clarity.

SpinDoctor 3.0.9 candidate
See SpinDoctor-3.0.9-Release-Notes.txt for circuit-state PDF figures and preset defaults.

SpinDoctor 3.0.8 candidate
See SpinDoctor-3.0.8-Release-Notes.txt for the PDF pipeline update.

SpinDoctor 3.0.7 candidate
See SpinDoctor-3.0.7-Release-Notes.txt for this update.

HISTORICAL SPINDOCTOR 3.0.6 NOTES
Run BOOT.cmd. Read SpinDoctor-3.0.6-Release-Notes.txt for automatic CPU/GPU
model sampling before folder receipts, completion stamps and report details.
Earlier releases and live workspaces are preserved.

Historical notes below refer to earlier releases.
==========================================================================
SPINDOCTOR 3.0.5 - CURRENT RELEASE
Run BOOT.cmd. Read SpinDoctor-3.0.5-Release-Notes.txt for the current behavior,
folder-report fix, zero-value explanation, safety bounds and limitations.
The original application and live workspace were not modified.

Historical notes below describe earlier releases; their version-specific claims
are retained as history. Current folder exports use streamed TXT archives.
==========================================================================

SPINDOCTOR 3.0.4 - CARRIER, INPUT AND STATE HARDENING
Prepared 2026-10-10 (America/Los_Angeles)

Run BOOT.cmd from this SpinDoctor folder. BOOT.cmd verifies the selected TIFF
or GIF before materializing the contained app. The primary terminal uses the
Windows .NET Framework and Windows PowerShell; Python is optional for legacy
reference experiments and tests. See native/requirements.txt for the tested
NumPy dependency. Read SpinDoctor-3.0.4-Release-Notes.txt for current fixes.

The original 3.0.3 release and its history are preserved at their original
location. This is a separate release under D:/Codex. Do not copy generated
audit test sessions into your live workspace. Existing workspace schemas are
retained; invalid or corrupted saved state is refused with its files preserved.

Circuit storage commands:
  q.circuit capacity           show capacity and saved circuit counts
  q.circuit capacity N         set capacity (1..1024; default 64)
  q.circuit prune presets KEEP remove oldest preset-owned circuits only
  q.circuit prune presets 0    clear preset-owned circuits; keep user circuits

q.compile synthesize / qc-198 remains unavailable: no validated approximate
Clifford+T synthesizer is installed. The installed alternatives are shown in
q.compile help and the unavailable-preset explanation. SpinDoctor remains a
classical FP64 simulator; represented operations and actual work are separate.

Historical 3.0.3 release notes follow. Their claims refer to that earlier build.
==========================================================================

SPINDOCTOR 3.0.3 - SESSION, SPEED AND REPORT HARDENING

Built from the 3.0.2-auto-reports carrier after an audit of all 1,775 TXT reports
produced by running every one of the 241 presets on 3.0.2. Only the five native
source files listed below, the version labels and these notes changed; every other
carrier entry is byte-identical to 3.0.2. Extract this package into its own folder
and start BOOT.cmd, exactly as before.

FIXED
1. 64-circuit wall in long sessions. 3.0.2 kept at most 64 saved circuit programs
   per workspace, so in one session the 66 circuit presets after qc-085 were refused
   ("Sequence would exceed 64 preserved circuit programs"). 3.0.3 records which
   circuits were created by q.preset runs ("preset-owned"). When a preset needs a
   slot, its sequence first issues an ordinary, reported command
   "q.circuit prune presets KEEP" that removes only the oldest preset-owned
   circuits, and lists them as prunedPresetCircuits on that sequence. Circuits you
   create yourself are never pruned automatically.
   New: q.circuit capacity [N]   show or set the slot limit (default 64, 1..1024)
   New: q.circuit prune presets [KEEP]   remove preset-owned circuits, oldest first
   When storage really is full, the error now says how many slots are used, how many
   are preset-owned, and which command frees or raises them.
2. Very slow circuit shots. Every gate of every replayed shot called
   TrancheRegister.Status(), which read Process.PrivateMemorySize64 (about 6-15 ms
   per call on Windows). That telemetry value is now sampled at most every 250 ms.
   Circuit runs are roughly 100x faster; histograms, seeds, counters and memory
   budgets are unchanged (the value is informational only).
3. q.compile synthesize (preset qc-198) stays disabled, with an explicit reason:
   Clifford+T synthesis to a stated operator-norm tolerance needs a number-theoretic
   synthesizer with independent error verification, which SpinDoctor does not
   include. No gate sequence is fabricated. The message lists installed
   alternatives (q.compile identity/classify/count, q.math gate-matrix).
4. Sequence step reports for synchronous commands printed an empty
   "Completed UTC:". They now show the completion time, or say "pending" for an
   accepted asynchronous job and "not completed" for a rejected command.
5. Command-history TXT/JSON report files are written atomically (temporary file,
   then replace), so an interruption cannot leave a truncated report.

UNCHANGED
Mathematics, gate conventions, random-number derivation, seeds, counters, report
layout, PDF exporter, loopback-only local service, carrier format and the
immutable original harness. No network features, no hardware or radio output, and
no physical quantum hardware is used or claimed: SpinDoctor is a classical FP64
simulator.

The release validation (all 241 presets in one shared session, TXT and PDF checks,
comparison with 3.0.2, independent reference check of every sampled histogram) is
supplied separately in validation/ next to this package.

PREVIOUS RELEASE NOTES (3.0.2)

SPINDOCTOR 3.0.2 - AUTOMATIC TXT AND PDF REPORTS

Extract this package into its own folder under D:\Codex and start BOOT.cmd.
The terminal now automatically requests both a .txt and a .pdf download for
every native command return, including help, status and rejected commands.
Asynchronous jobs request an accepted report pair and a completed report pair.
Very fast jobs are also checked for completion, even if their returned status
already shows that they finished.

PDF reports use the native exporter and the immutable accepted/completed
archive snapshots. Exporting does not rerun calculations, gates or measurements.
Automatic PDFs queue separately from terminal commands. Repeated job polling
does not request duplicate report pairs. Failed TXT/PDF requests are shown in
the terminal with a retry button; retrying does not rerun the original command.
Manual selected, session, archived and circuit reports remain available.

If your browser asks, allow multiple automatic downloads for the local
calculator page. A browser may block a download after the app requests it;
the app cannot verify that the browser saved a file. Manual downloads remain
available if the browser blocks automatic downloads.

Batch 001 audit
All 17 supplied command TXT files were parsed: 85 complete JSON sections.
The 12 chronological gate matrices independently agree to within 2.78e-16.
Recorded represented gate credits total 6,000,000,000,000,000.
The recorded verification has 10,252 CPU/GPU comparisons: 10,240 requested
samples plus 12 boundary addresses, zero recorded errors, no measurements.
These are recorded historical GPU results, not a fresh GPU run in this release.
No mathematical calibration change was indicated by this batch.
The PDF issue was the missing automatic PDF request in the browser interface.

Original input files and the original candidate ZIP were preserved.
Prior validation reports are retained as historical evidence. Current
automatic-report validation is supplied separately in validation/.

PREVIOUS RELEASE NOTES (3.0.1)

SPINDOCTOR 3.0.1 - REPORT AND MEASUREMENT HARDENING

Start BOOT.cmd from the SpinDoctor folder. Close the previous service console
first. The update replaces static carriers and interface files only; workspace,
original inputs, seeds, checkpoints, replay records and history are preserved.

WHY THE SUPPLIED OUTPUTS WERE INCOMPLETE
The supplied PDF is an Edge page-print of the dashboard. Its GHZ29 X job failed
under the512MiB budget after20measuredqubits/21draws/0complete shots.
An accepted command was incorrectly labeled OK after the job failed; the viewer
exported a bounded preview before archive recovery. No histogram existed.

WHAT CHANGED
* Exact projective measurement separates each measured qubit from the remaining
  factor. No amplitude cutoff, approximation or entanglement loss is introduced.
  GHZ29 X now completes1,024shots under512MiB with even parity. Other dense
  states still require their actual memory; full500T-address storage is not used.
* Failed/cancelled/partial job outcome is distinct from command acceptance and
  archive-stage completion. Partial measurements and consumed draws are retained.
* Measured-domain seed and counter labels distinguish circuit/tranche/family.
* Native selected/session/archive PDF controls download actual vector reports.
  The server resolves archive references, rejects altered JSON/TXT archives and
  retains each job's immutable gates and entire completed-shot histogram.
* Browser Print uses a dedicated selected-report SVG layout, explicitly labeled
  as a bounded browser snapshot. Native PDFs carry full returned numeric data.
* Figures include circuit wires/gates, amplitude/probability/phase charts, spin
  views, complex matrices and exact entries, distributions and histogram tables.
  Missing numerics are labeled; errors do not create simulated outcomes.
* Export never reruns calculations, gates, queries or measurements. All241
  presets, commands and existing checkpoint schemas remain supported.

VALIDATION
Independent phase/amplitude/collapse checks325,613; legacy32-gate checks9,256.
Native archive/historical snapshot/tamper/schema/no-rerun checks29; native HTTP
40checks; actual calculator/circuit delivery282checks; UI fixtures11+18checks.
Native PDF pages rendered with Poppler and inspected. Browser screenshot/print
rendering was not available: the isolated browser could not reach the loopback
service, and local-file navigation was blocked by its security policy.
See validation/report-hardening-* for precise source hashes and test scope.

HISTORICAL RETURN LIMITATION
Rejected projection writes were not all counted by3.0.0. Their exact historical
number cannot be recovered.3.0.1 retains temporary projection writes on failure.
The repaired audit PDF preserves old failed returns, without changing their
accounting or replacing them with the new independently validated successes.

EARLIER COMPATIBLE FEATURES AND SCOPE

SPINDOCTOR QUANTUM CALCULATOR CANDIDATE

START / UPDATE IN PLACE
Run BOOT.cmd from the extracted folder (or BOOT.cmd gif). Keep the local
service console open. The TIFF and GIF contain the same validated payload.
Close an older running console and boot again to load this candidate.
The update writes static carriers/programs only: existing mutable workspace,
seeds, counters, checkpoints, named circuits and history are preserved.
Do not move or remove your existing workspace during the update.

EXACTLY 241 PRESETS
The attached 241-entry manifest is preserved byte-for-byte. Its searchable
dropdown is beside the terminal, with category/search/favorites/recent views.
Selection inserts the full command/sequence and argument preview; it does
not execute. Original observed/proposed labels and current native capability
labels are separate. A proposed item stays visible, but cannot claim an
installed numerical backend. Approximate Clifford+T RZ synthesis (qc-198)
remains proposed; no dummy compiled sequence or unsupported execution credit.
Large coherent examples are conditional on state growth and actual RAM.

ORDERED SEQUENCES AND FOLDER BAR
Confirm beside the folder path fills, without running:
  q.folder run path="D:/Quantum-Inputs" downloads=true purge=true
The adjacent Individual file downloads checkbox sets downloads=true/false.
Run starts an asynchronous, nonrecursive, deterministic filename-ordered job.
One file executes at a time; dependent commands wait for their matching native
gate, verification or circuit job. Failures/cancellation stop dependent work
and retain completed counts and replay information.
Supported folder extensions: .json .qasm .txt .csv .pdf, at most 128 files;
each file is at most 4 MiB and expands to at most 128 quantum commands.
Absolute local-drive paths only; reparse points and arbitrary code are refused.
Nested preset commands expand to fresh unique circuit names. Inputs do not
run shell statements, external includes, document instructions or prose.
  q.folder status
  q.folder cancel
  q.sequence status
  q.sequence cancel
  q.sequence export
  q.preset list
  q.preset show qc-001
  q.preset run qc-001

DOWNLOADS / PURGE / HISTORY
Every terminal command gets an automatic TXT result download, including
help, status, resource/theory commands and errors. Accepted background-job
reports and matched completion reports are distinct. Folder mode additionally
offers a complete TXT report for each file when its checkbox is enabled.
Browsers may require allowing multiple downloads; reports remain available
in the Return explorer/native history and through explicit download controls.
Large output arrays are not kept as a whole-family dataset. Each folder file
captures its report, releases transient coherent output and compiler buffers,
then proceeds. Managed memory is reusable after references are released;
OS reclamation follows the .NET runtime. Original source files are never
deleted. Preserve small replay descriptors, report/history metadata, seeds,
measurement counters, programs and checkpoints so results stay accountable.
  q.history list
  q.history show REPORT_ID
  q.history export REPORT_ID
The history page is indexed/paged so earlier reports remain discoverable.
Clear scrollback does not clear return history. Existing browser storage keys
remain intact; session TXT/JSON exports are available for browser-held returns.

INDEXED INPUT / EQUATION PAGE
The second indexed page in the interface documents the formats and displays
the equation catalogue. Upload alone never executes. Preview then explicitly
Import a circuit or Run a command sequence:
  q.input show ID
  q.input evaluate ID
  q.input import ID
  q.input run ID
  q.input import CSV_ID row 0 base CIRCUIT_ID
PDF: one embedded UTF-8 .json/.qasm/.txt/.csv program, or an explicit tagged
ASCII text block SPINDOCTOR-JSON-BEGIN ... SPINDOCTOR-JSON-END (also QASM,
TXT, CSV). Plain and single-Flate PDF streams are supported. Encrypted PDFs,
scans/OCR, arbitrary prose, unsupported font/object-stream encodings and
multiple ambiguous embedded programs are not executable input.
Original PDF bytes and SHA256 are preserved and downloaded as application/pdf.
Text attachments have a 5 MiB bound, PDFs 4 MiB; the preserved upload catalogue
supports 32 sources / 30 MiB total. Folder compilation is transient and does
not fill that upload catalogue.

SEQUENCE FORMAT EXAMPLES
JSON:
  {"commands":["q.math density amplitudes=[1/sqrt(2),i/sqrt(2)]",
               "q.preset run qc-001"]}
TXT (one command per line):
  # SPINDOCTOR-SEQUENCE
  q.math probabilities amplitudes=[1/sqrt(2),i/sqrt(2)]
  q.resource memory n=29 representation=statevector dtype=complex128 units=binary
CSV: a single command header, followed by properly quoted command cells.
Bare parameter CSVs require an explicit circuit base and row; they are not
guessed into runnable circuits by folder mode. Standard circuit JSON,
OpenQASM2 unitary subset and legacy TXT circuit configurations also work.
Safe expressions support documented finite real/complex constants/functions;
angles precede qubit operands and q0 is the least-significant basis bit.

VISUAL PDF REPORTS
Selected, session and circuit report controls produce self-contained vector
PDFs. Native returned values drive complex amplitude/phase bars, density/gate
matrix magnitude-phase sheets, Bloch/spin projections, spectra, outcome
histograms, circuit diagrams, exact gate operands and accounting tables.
The report identifies theory, resource estimates, partial jobs and commands
that did not measure. Exporting never reruns calculations or random shots.
TXT/JSON retain full native values when a PDF plots a labeled bounded prefix.

MATHEMATICS / BACKEND ROUTING
Procedural grid: independent single-qubit states, shared 2x2 composed gates,
requested CPU/OpenCL samples; no implicit whole-grid enumeration or entanglement.
Selected tranche/circuit: exact coherent factor/sparse/segmented dense FP64
backend, 1..29 qubits, 32 supported gate names, actual complex phase and joint
measurement correlations. There is no installed stabilizer-tableau or general
tensor-network approximation engine; compatible circuits use coherent simulation.
Math/query: bounded complex vectors/matrices; full joint query analysis at most
16 qubits / 65536 amplitudes, one-qubit native reductions up to 29 qubits.
Noise: bounded density-matrix channels, not procedural pure-state descriptors.
QEC/compile/algorithm: bounded validated numerical models and explicit theory
results. Phase estimation uses a small actual coherent phase-eigenstate circuit;
it is not arbitrary Shor factoring. General entanglement cannot inherit the
500-trillion-address product-family throughput.

SOURCE COVERAGE AND GAPS
EquationCatalogue.json contains 88 guide formulas/examples, 40 article topic
audits and all 14 article sections, with sources, conventions, status and scope.
The supplied article TXT has 170 erased inline-symbol/math gaps; distinct lost
equations cannot be uniquely recovered from those blanks. The original guide
and article are contained for inspection. Established guide formulas are
implemented/indexed; advanced CV/qudit/hardware/decoder and source-specific
theorem topics remain explicitly reference/proposed where no backend exists.
No missing source equation or contested theorem is presented as validated physics.

RAM / ACTUAL WORK
  q.resource headroom n=29 representation=statevector
  q.circuit memory auto
  q.tranche memory auto
Auto budgets use momentary Windows available physical RAM, minus a 2 GiB OS
reserve and 256 MiB additional overhead. Only arrays needed by the computation
are allocated; dummy RAM filling cannot increase calculation throughput.
Fresh folder circuit jobs use this policy; explicit older budgets remain saved.
Circuit peak planning includes cached output, a shot state and transactional
work. A dense 29-qubit complex128 vector alone is 8 GiB; its combined peak may
need about 24 GiB plus overhead. A resource estimate is not an allocation.
The 500-trillion-state family is procedural; materializing two complex128
amplitudes per address would require 16 PB of output. Streaming reduces peak
memory but does not eliminate actual calculation/write time for every value.
Represented family credits, actual CPU/GPU states, verified samples, coherent
gate calls, amplitude writes, tensor products, shot draws, query evaluations,
calculator returns and elapsed times remain separate. Physical quantum gates: 0.
The uncapped q.verify samples command streams bounded batches across the finite
domain; cancellation preserves partial coverage, never labels it full verification.

VALIDATION
See SPINDOCTOR-VALIDATION.json / validation/ for exact executed source hashes,
mathematical checks, CPU/GPU agreement, preset/sequence/folder/input/HTTP tests,
rendered PDF checks, image-carrier round trips and preservation evidence.
UI tests exercise the actual contained JavaScript/control bindings with a minimal
DOM and genuine native calculations. A rendered browser layout is not claimed.
