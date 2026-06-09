"""Sanity-check the v2 corruption set actually fails."""
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, 'scripts')
from phase6_h4_indent_v2 import CORRUPTIONS, compile_calor, FIXTURE  # noqa: E402

src = FIXTURE.read_text(encoding='utf-8')
for cname, cfn in CORRUPTIONS:
    corrupted = cfn(src)
    with tempfile.TemporaryDirectory() as td:
        ok, err = compile_calor(corrupted, Path(td))
    head = err.split('\n')[0][:120] if err else ''
    status = 'OK(no-err)' if ok else 'FAIL'
    print(f'{cname:18s}  {status:10s}  {head}')
