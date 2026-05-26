$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
python "$here/verify_corpus.py" @args
exit $LASTEXITCODE
