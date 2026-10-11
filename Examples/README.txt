CSV examples for SpinDoctor 3.1.1.

weather-example.csv, gate-measurements.csv and circuit-measurements.csv contain
generated demonstration data, not observed weather or hardware measurements.
Use q.input table followed by explicit q.input fit target/features choices.
No causal law, independent validation or future forecast is claimed.

CSV-derived fits and declared gates/circuits provide native equation text and
conventional mathematical notation. The interface lets you edit and copy a
local equation draft, or reset it to the native text. Draft edits never change
the preserved input, coefficients, figures or authenticated PDF/TXT/CSV receipt,
and never run a calculation. Edit the source input and explicitly Fit/Import/Run
to obtain a new native result. An empirical fitted equation remains distinct
from a declared circuit's ordered operator/state/Born/Bloch expressions.

gate-rotated-qubit.csv and gate-bell.csv are explicit ordered gate-list circuit
data. They default to 1024 classical simulation shots in Z. Upload is inert;
q.input import defines the circuit, and q.input run explicitly simulates it.
The rotated RY(pi/2) state has a +X Bloch arrow. An entangled Bell qubit has a
legitimate zero reduced Bloch vector and purity 1/2.

command-equation-example.csv is a separate explicit command-column format for
supported q.math commands. It executes only after an explicit run action.

See SpinDoctor-3.1.1-CSV-Input-Guide.txt for exact schemas, provenance, limits,
missing-data/rank handling and report workflow.
