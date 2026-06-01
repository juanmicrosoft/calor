"""Phase 1: mechanical replacement of Parser.cs block-end checks.

Patterns:
  Check(TokenKind.EndXxx)            -> IsBlockEnd(TokenKind.EndXxx)
  Expect(TokenKind.EndXxx)           -> ExpectBlockEnd(TokenKind.EndXxx)

Leaves Match(TokenKind.EndXxx) alone — those are conditional consumes,
not block-end checks. Match patterns at lines 3297, 3312 etc. are
intentional optional closer consumption inside case bodies.

Idempotent: re-running on already-converted file is a no-op.
"""
import re
import sys
from pathlib import Path

P = Path('src/Calor.Compiler/Parsing/Parser.cs')
src = P.read_text(encoding='utf-8')

# Counter
n_check = 0
n_expect = 0

# Pattern 1: Check(TokenKind.EndXxx)  -> IsBlockEnd(TokenKind.EndXxx)
# Must not be inside IsBlockEnd(...) already.
def repl_check(m):
    global n_check
    n_check += 1
    return f'IsBlockEnd(TokenKind.{m.group(1)})'

src = re.sub(
    r'(?<!IsBlock)(?<!\w)Check\(TokenKind\.(End[A-Z][a-zA-Z0-9]*)\)',
    repl_check,
    src,
)

# Pattern 2: Expect(TokenKind.EndXxx) -> ExpectBlockEnd(TokenKind.EndXxx)
def repl_expect(m):
    global n_expect
    n_expect += 1
    return f'ExpectBlockEnd(TokenKind.{m.group(1)})'

src = re.sub(
    r'(?<!ExpectBlock)(?<!\w)Expect\(TokenKind\.(End[A-Z][a-zA-Z0-9]*)\)',
    repl_expect,
    src,
)

P.write_text(src, encoding='utf-8')
print(f'replaced Check(End*)  -> IsBlockEnd:  {n_check} sites')
print(f'replaced Expect(End*) -> ExpectBlockEnd: {n_expect} sites')
print(f'total: {n_check + n_expect}')
