# Optional native Python experiments

The main SpinDoctor terminal launches from `BOOT.cmd` using Windows PowerShell and
the installed .NET Framework. It does not need Python or NumPy.

The optional Python CPU/OpenCL experiments require a 64-bit Python installation
that supports NumPy 2.3.5 and an installed OpenCL GPU driver. Their tests use the
standard-library `unittest` runner; pytest is not required. From the extracted
`mvp` folder, run:

```powershell
python -m pip install -r native\requirements.txt
python -m unittest discover -s native -p "test_*.py" -v
python native\test_cpu_gpu.py
python native\test_shared_epoch_xpu.py
```

The first command pins the tested NumPy version. The unittest suite checks the
probability-sheet model without dispatching a GPU job. The last two commands
perform small real CPU/GPU runs with a 1 MiB test reservoir; they require a working
OpenCL GPU and fail explicitly when one is unavailable. They write validation
JSON beside the extracted files. Their measured timing is specific to the machine
and current workload.

The optional local viewer servers bind only to `127.0.0.1`, select a random port,
and require the generated token and matching browser origin for commands. Keep
the launcher running while using the viewer; Ctrl+C stops it.
