$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
python "$here/validate_task_template.py" @args
exit $LASTEXITCODE
