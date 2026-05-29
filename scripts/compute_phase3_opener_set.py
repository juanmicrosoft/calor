#!/usr/bin/env python3
"""
compute_phase3_opener_set.py — enumerate Phase 3 structural openers.

Implements Prereq 1 disambiguation algorithm from
docs/plans/phase-3-indentation-validation-plan.md (v4.1):

    A keyword X is a "structural opener" subject to RFC §4.2.3's unified
    construct table iff ALL THREE hold:

      1. X is defined in the `Keywords` dictionary of Lexer.cs
         OR is handled by a named special-case scanner branch
         (currently §PP, §/PP, §PPE at Lexer.cs:640-641, 718-731, 905-906).

      2. X has a matching `/X` closer entry in the same dictionary
         OR is in the special-case set.

      3. X's TokenKind is referenced from a `ParseStatement` dispatch
         branch (or its equivalent for class members, expression-position
         closers, etc.) in Parser.cs at the SHA frozen in §9 — i.e., X
         opens a *statement-context* block, not just an argument list or
         expression context.

    This rule explicitly EXCLUDES:
      - expression-context closers (§/C, §/THIS, §/BASE, §/NEW, §/ANON, §/INTERP)
      - mid-block markers (§EI, §EL, §CA, §FI, §K, §WHEN)

Aliases (/SW for /W, /ENUM for /EN) are merged with their canonical form.
The opener §IF is a known special case: it has no matching §/IF closer
(the chain closer is §/I which closes the entire IF/EI/EL chain).

Output: JSON list of opener records to stdout, sorted by `opener`. Each record:
  {
    "opener": "F",                  # canonical opener keyword (no §)
    "closer": "/F",                 # matching closer keyword (no §), or null if special
    "token_kind": "Func",           # C# TokenKind enum value name
    "end_token_kind": "EndFunc",    # C# TokenKind enum value name for closer, or null
    "dispatch_contexts": ["statement", "class_member", "module_member", "expression"],
    "treatment": "dedent-closed" | "closer-retained" | "not-applicable",
    "rationale": "...",
    "lexer_line": 24,
    "lexer_closer_line": 44
  }

Usage:
    python scripts/compute_phase3_opener_set.py [--lexer PATH] [--parser PATH] [--out PATH]

Default paths target the repo-local Calor.Compiler sources.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_LEXER = REPO_ROOT / "src" / "Calor.Compiler" / "Parsing" / "Lexer.cs"
DEFAULT_PARSER = REPO_ROOT / "src" / "Calor.Compiler" / "Parsing" / "Parser.cs"

# Special-case scanner entries that bypass the Keywords dict.
# Sourced from Lexer.cs:640-641, 718-731, 905-906 at the pinned SHA.
SPECIAL_CASE_ENTRIES = {
    "PP":   ("Preprocessor",      "/PP", "EndPreprocessor"),
    "PPE":  ("PreprocessorElse",  None,  None),
}

# Mid-block markers — chain continuations, not openers.
MID_BLOCK_MARKERS = {"EI", "EL", "CA", "FI", "K", "WHEN", "when"}

# Expression-position closers that should NOT be in the Phase 3 table.
EXPRESSION_CONTEXT_CLOSERS = {"/C", "/THIS", "/BASE", "/NEW", "/ANON", "/INTERP"}

# Alias merging: closer key on left, canonical key on right.
# E.g. /SW is an alias for /W (both → TokenKind.EndMatch). We collapse to canonical.
CLOSER_ALIASES = {
    "/SW":   "/W",
    "/ENUM": "/EN",
}

# Opener-side aliases (multiple openers map to same TokenKind).
OPENER_ALIASES = {
    "SW":   "W",
    "ENUM": "EN",
    "WR":   "WHERE",
}


@dataclass
class Opener:
    opener: str
    closer: str | None
    token_kind: str
    end_token_kind: str | None
    dispatch_contexts: list[str] = field(default_factory=list)
    treatment: str = "tbd"
    rationale: str = ""
    lexer_line: int | None = None
    lexer_closer_line: int | None = None
    _end_dispatch_contexts: set[str] = field(default_factory=set, repr=False, compare=False)


KEYWORD_LINE_RE = re.compile(
    r'^\s*\["(?P<key>[^"]+)"\]\s*=\s*TokenKind\.(?P<kind>\w+)',
)


def parse_keywords(lexer_source: str) -> dict[str, tuple[str, int]]:
    """Returns mapping: keyword -> (TokenKind, line number)."""
    out: dict[str, tuple[str, int]] = {}
    in_dict = False
    for i, line in enumerate(lexer_source.splitlines(), start=1):
        if "Dictionary<string, TokenKind> Keywords" in line:
            in_dict = True
            continue
        if not in_dict:
            continue
        # End of dictionary is the next "};" at indent ≤ 4.
        stripped = line.rstrip()
        if stripped.endswith("};") and not stripped.lstrip().startswith("["):
            break
        m = KEYWORD_LINE_RE.match(line)
        if m:
            out[m.group("key")] = (m.group("kind"), i)
    return out


# Match `Check(TokenKind.X)`, `case TokenKind.X`, `peek == TokenKind.X`, etc.
TOKENKIND_REF_RE = re.compile(r"TokenKind\.(\w+)")
METHOD_HEADER_RE = re.compile(r"^\s+(public|private|internal|protected)\s+[^;]+?\b(?P<name>\w+)\(")


def parse_dispatch_contexts(parser_source: str) -> dict[str, set[str]]:
    """
    Returns mapping: TokenKind -> set of dispatch-context tags.

    Context tags we use:
      "statement"        — `Check(TokenKind.X)` inside ParseStatement body
      "class_member"     — `Check(TokenKind.X)` inside ParseClassDefinition / ParseInterfaceDefinition
      "module_member"    — `Check(TokenKind.X)` inside ParseModule (top-level)
      "expression"       — `Check(TokenKind.X)` inside ParsePrimaryExpression / ParseExpression
      "closer_consumed"  — `Expect|Check(TokenKind.X)` where X is an EndXxx token (strong
                           signal that the parser actively closes a block with this token)
    """
    lines = parser_source.splitlines()

    method_spans: list[tuple[str, int, int]] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        m = METHOD_HEADER_RE.match(line)
        if m:
            name = m.group("name")
            j = i
            while j < len(lines) and "{" not in lines[j]:
                j += 1
            if j >= len(lines):
                i += 1
                continue
            depth = 0
            k = j
            while k < len(lines):
                depth += lines[k].count("{") - lines[k].count("}")
                if depth == 0:
                    break
                k += 1
            method_spans.append((name, i, k))
            i = k + 1
        else:
            i += 1

    def context_for(method_name: str) -> str:
        n = method_name
        if n == "ParseStatement":
            return "statement"
        if n in (
            "ParseClassDefinition", "ParseInterfaceDefinition",
            "ParseStructDefinition", "ParseRecordDefinition",
        ):
            return "class_member"
        if n in ("ParseModule",):
            return "module_member"
        if n in ("ParsePrimaryExpression", "ParseExpression", "ParseUnary", "ParseLisp"):
            return "expression"
        return ""

    result: dict[str, set[str]] = {}
    for name, start, end in method_spans:
        ctx = context_for(name)
        if not ctx:
            continue
        body = "\n".join(lines[start : end + 1])
        for kind in TOKENKIND_REF_RE.findall(body):
            result.setdefault(kind, set()).add(ctx)

    # Also detect "closer_consumed": any reference to TokenKind.EndXxx anywhere in Parser.
    # End-tokens are unique and serve only to close blocks; any reference is evidence the
    # parser actively closes that block.
    closer_consumed_re = re.compile(r"TokenKind\.(End\w+)\b")
    for m in closer_consumed_re.finditer(parser_source):
        result.setdefault(m.group(1), set()).add("closer_consumed")

    return result


def classify_treatment(opener: Opener) -> tuple[str, str]:
    """Returns (treatment, rationale)."""
    op = opener.opener
    if op == "IF":
        return (
            "dedent-closed",
            "if/elseif/else chain — see RFC §4.2 subtlety 1; chain closer §/I retained as legacy-form alias",
        )
    # Argument-form openers — these take an explicit argument expression, not a body.
    if op in {"A", "I", "O", "E", "R", "B", "Q", "S"}:
        return ("not-applicable", "single-line atomic form; no body to dedent-close")
    # Inline operator/literal forms.
    if op in {"P", "Pf", "IDX", "LEN", "ADD", "PUT", "REM", "SETIDX", "CLR", "INS",
              "HAS", "KEY", "VAL", "CNT", "PUSH", "ASSIGN", "DEFAULT", "BK", "CN",
              "GOTO", "LABEL", "TH", "RT", "AS", "BODY", "END_BODY",
              "WHERE", "WR", "EXT", "IMPL", "VR", "OV", "AB", "SD", "FLD",
              "EX", "TD", "FX", "HK", "US", "UB", "AU", "FILE", "TASK", "DATE",
              "CHOSEN", "REJECTED", "REASON", "FC", "AS",
              "ASYNC", "SUB", "UNSUB", "YIELD", "YBRK",
              "DEREF", "ADDR", "SIZEOF", "IV",
              "CX", "SN", "DP", "BR", "XP", "SB", "PT", "LK",
              "ROW", "PMATCH", "PPOS", "PPROP", "PREL", "PLIST", "VAR", "REST",
              "PROOF"}:
        return ("not-applicable", "inline form; emits a single token, not a block")
    # Expression-context openers — emit values, not blocks.
    if op in {"SM", "NN", "OK", "ERR", "C", "THIS", "BASE", "NEW", "ANON", "INTERP", "LAM"}:
        return ("not-applicable", "expression form; appears inside expressions, not as a block")
    # Special preprocessor markers — conditional compilation, not control flow.
    if op in {"PP", "PPE"}:
        return ("closer-retained", "preprocessor directive; closer carries the condition string for grep/audit")
    # Default for body-bearing constructs.
    if opener.closer:
        return ("dedent-closed", "body-bearing construct; dedent closes the block")
    return ("not-applicable", "no matching closer in Keywords dict")


def build_openers(
    keywords: dict[str, tuple[str, int]],
    dispatch: dict[str, set[str]],
) -> list[Opener]:
    # Apply opener aliases: if X is alias for Y, drop X.
    aliased_out = set(OPENER_ALIASES.keys())

    # Pre-compute closer index.
    closer_index: dict[str, tuple[str, int]] = {}  # closer-key -> (TokenKind, line)
    for k, (kind, ln) in keywords.items():
        if k.startswith("/"):
            # Apply closer aliases too: /SW collapses to /W.
            canonical = CLOSER_ALIASES.get(k, k)
            closer_index.setdefault(canonical, (kind, ln))

    openers: list[Opener] = []
    seen_openers: set[str] = set()

    for key, (kind, line) in keywords.items():
        if key.startswith("/"):
            continue
        if key in MID_BLOCK_MARKERS:
            continue
        if key in aliased_out:
            continue
        if key in seen_openers:
            continue
        seen_openers.add(key)

        closer_key = "/" + key
        # Check for canonical-aliased closer.
        canonical_closer = CLOSER_ALIASES.get(closer_key, closer_key)
        closer_data = closer_index.get(canonical_closer)
        # IF has no /IF; chain closer is /I.
        if key == "IF":
            closer_data = closer_index.get("/I")
            canonical_closer = "/I"

        op = Opener(
            opener=key,
            closer=canonical_closer if closer_data else None,
            token_kind=kind,
            end_token_kind=closer_data[0] if closer_data else None,
            lexer_line=line,
            lexer_closer_line=closer_data[1] if closer_data else None,
            dispatch_contexts=sorted(dispatch.get(kind, set())),
        )
        if closer_data:
            op._end_dispatch_contexts = dispatch.get(closer_data[0], set())
        op.treatment, op.rationale = classify_treatment(op)
        openers.append(op)

    # Add special-case entries (§PP, §PPE).
    for sp_key, (sp_kind, sp_closer, sp_end_kind) in SPECIAL_CASE_ENTRIES.items():
        op = Opener(
            opener=sp_key,
            closer=sp_closer,
            token_kind=sp_kind,
            end_token_kind=sp_end_kind,
            dispatch_contexts=sorted(dispatch.get(sp_kind, set())),
        )
        if sp_end_kind:
            op._end_dispatch_contexts = dispatch.get(sp_end_kind, set())
        op.treatment, op.rationale = classify_treatment(op)
        openers.append(op)

    openers.sort(key=lambda o: o.opener)
    return openers


def filter_phase3_openers(openers: list[Opener]) -> list[Opener]:
    """Apply Prereq 1's 3-part criterion."""
    out: list[Opener] = []
    for op in openers:
        has_closer = op.closer is not None
        in_special = op.opener in SPECIAL_CASE_ENTRIES
        # Criterion 1+2: must have matching closer OR be in special-case set.
        if not (has_closer or in_special):
            continue
        # Criterion 3 (v4.1 disambiguation): we use a stronger test than v3.
        # An opener is "statement/declaration context" iff ANY of:
        #   (a) it appears in a known dispatch method (ParseStatement, ParseModule,
        #       ParseClassDefinition, ParseInterfaceDefinition),
        #   (b) its END token is actively consumed by the parser (Expect/Check/Match),
        #       which means there is a body the parser closes,
        #   (c) it is in the named special-case set (§PP).
        statement_like = any(
            c in {"statement", "class_member", "module_member"} for c in op.dispatch_contexts
        )
        end_consumed = "closer_consumed" in op._end_dispatch_contexts
        # Promote the end_consumed signal into the visible dispatch_contexts list so
        # downstream reviewers can audit the evidence.
        if end_consumed:
            if "closer_consumed" not in op.dispatch_contexts:
                op.dispatch_contexts = sorted({*op.dispatch_contexts, "closer_consumed"})
        # If only expression-context is set AND no end-consumption AND not special,
        # then this is genuinely an expression-position closer/marker — exclude.
        purely_expression = (
            not statement_like
            and not end_consumed
            and not in_special
            and "expression" in op.dispatch_contexts
        )
        if purely_expression:
            op.treatment = "not-applicable"
            op.rationale = (
                f"expression-position only; TokenKind.{op.token_kind} not consumed in "
                "statement/declaration context"
            )
            out.append(op)
            continue
        # If we have no evidence at all, mark NA with explicit "no evidence" rationale.
        if not (statement_like or end_consumed or in_special):
            op.treatment = "not-applicable"
            op.rationale = (
                f"no dispatch or closer-consumption evidence for TokenKind.{op.token_kind}"
            )
        out.append(op)
    return out


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--lexer", type=Path, default=DEFAULT_LEXER)
    p.add_argument("--parser", type=Path, default=DEFAULT_PARSER)
    p.add_argument("--out", type=Path, default=None, help="Write JSON to this path (default stdout)")
    p.add_argument(
        "--markdown",
        type=Path,
        default=None,
        help="Also emit a markdown table at this path (for RFC §4.2.3)",
    )
    args = p.parse_args()

    if not args.lexer.exists():
        print(f"lexer source not found: {args.lexer}", file=sys.stderr)
        return 2
    if not args.parser.exists():
        print(f"parser source not found: {args.parser}", file=sys.stderr)
        return 2

    keywords = parse_keywords(args.lexer.read_text(encoding="utf-8"))
    dispatch = parse_dispatch_contexts(args.parser.read_text(encoding="utf-8"))

    openers = build_openers(keywords, dispatch)
    openers = filter_phase3_openers(openers)

    records = []
    for o in openers:
        d = asdict(o)
        d.pop("_end_dispatch_contexts", None)
        records.append(d)
    json_payload = json.dumps(records, indent=2, ensure_ascii=False)

    if args.out:
        args.out.write_text(json_payload + "\n", encoding="utf-8")
        print(f"wrote {len(records)} opener records to {args.out}", file=sys.stderr)
    else:
        sys.stdout.write(json_payload + "\n")

    if args.markdown:
        emit_markdown(records, args.markdown)
        print(f"wrote markdown table to {args.markdown}", file=sys.stderr)

    n_dedent = sum(1 for r in records if r["treatment"] == "dedent-closed")
    n_retained = sum(1 for r in records if r["treatment"] == "closer-retained")
    n_na = sum(1 for r in records if r["treatment"] == "not-applicable")
    print(
        f"summary: {len(records)} entries — dedent-closed={n_dedent} "
        f"closer-retained={n_retained} not-applicable={n_na}",
        file=sys.stderr,
    )
    return 0


def emit_markdown(records: list[dict], path: Path) -> None:
    lines: list[str] = []
    lines.append("| Opener | Closer | TokenKind | Treatment | Dispatch | Rationale |")
    lines.append("|--------|--------|-----------|-----------|----------|-----------|")
    for r in records:
        opener = f"`§{r['opener']}`"
        closer = f"`§{r['closer']}`" if r["closer"] else "—"
        kind = f"`{r['token_kind']}`"
        treatment = r["treatment"]
        dispatch = ",".join(r["dispatch_contexts"]) or "—"
        rationale = r["rationale"].replace("|", "\\|")
        lines.append(f"| {opener} | {closer} | {kind} | {treatment} | {dispatch} | {rationale} |")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
