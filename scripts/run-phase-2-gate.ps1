# Thin wrapper for scripts/run_phase_2_gate.py.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
python "$here/run_phase_2_gate.py" @args
exit $LASTEXITCODE
