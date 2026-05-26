$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
python "$here/verify_phase1.py" @args
exit $LASTEXITCODE
