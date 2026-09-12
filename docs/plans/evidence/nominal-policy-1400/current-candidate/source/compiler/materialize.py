#!/usr/bin/env python3
"""Materialize D1 #1400 compiler measurement sources into a detached scratch worktree.

This script is intentionally a source transform, not a runner. It refuses the
author/main worktree, refuses attached branches, verifies the accepted source8f
compiler file hashes, and applies only exact text replacements with count and
hash assertions.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from typing import Callable


ACCEPTED_SOURCE_COMMIT = "8f9891a1a07f786a76a293017959c3edf28412bf"
SOURCE_ARTIFACT_MAIN_WORKTREE = Path(
    "/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/nominal-policy-1400"
).resolve()
SHARED_REPOSITORY_ROOT = Path("/Users/juanrivera/sources/repos/juanmicrosoft/calor").resolve()

PINNED_INPUT_SHA256 = {
    "src/Calor.Compiler/Binding/Binder.cs": "641bf91a4f3b5c528d7b3c8aed3cc2ec687eb9f6a53e57380d0ee05a960e7dc3",
    "src/Calor.Compiler/Binding/NullabilityChecker.cs": "af932a552e7e738b7132faa5d201af1ee0d2d7c83740f1cce2c1c30a26410785",
    "src/Calor.Compiler/Binding/Scope.cs": "cdf664997accd073527820c216ce03f4287eaf2dc2bd56879c3d2c20e6b2252c",
    "tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs": "19ab767553f942d0488856b88dd9ff322ca04a2ac4f36d8659f6367bcad64a1a",
}

MODES = {"current", "shadow-annotated", "shadow-conservative"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worktree", required=True, type=Path, help="Detached scratch worktree to modify.")
    parser.add_argument("--mode", required=True, choices=sorted(MODES), help="Measurement mode to materialize.")
    parser.add_argument(
        "--accept-verified8f-file-hashes",
        action="store_true",
        help=(
            "Allow HEAD other than source8f only when every pinned compiler/harness "
            "file hash exactly matches source8f. Still requires a clean detached worktree."
        ),
    )
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    worktree = args.worktree.resolve()
    git_root = git(worktree, "rev-parse", "--show-toplevel").strip()
    root = Path(git_root).resolve()
    if root != worktree:
        fail(f"--worktree must name the git worktree root exactly; got {worktree}, root is {root}")
    refuse_forbidden_root(root)
    assert_detached_clean_source(root, allow_verified_hashes=args.accept_verified8f_file_hashes)

    manifest: dict[str, object] = {
        "schema": "calor.d1.materialization.v1",
        "acceptedSourceCommit": ACCEPTED_SOURCE_COMMIT,
        "mode": args.mode,
        "worktree": str(root),
        "transformations": [],
        "addedSources": [],
    }

    write_mode_source(
        root,
        script_dir,
        "D1NominalPolicyCapture.cs.txt",
        "src/Calor.Compiler/Binding/D1NominalPolicyCapture.cs",
        args.mode,
        manifest,
    )
    write_mode_source(
        root,
        script_dir,
        "D1RoundTripAttemptArchive.cs.txt",
        "tools/Calor.RoundTrip.Harness/D1RoundTripAttemptArchive.cs",
        args.mode,
        manifest,
    )

    transform_file(root, "src/Calor.Compiler/Binding/Binder.cs", transform_binder(args.mode), manifest)
    transform_file(root, "src/Calor.Compiler/Binding/NullabilityChecker.cs", transform_nullability(args.mode), manifest)
    transform_file(root, "src/Calor.Compiler/Binding/Scope.cs", transform_scope(), manifest)
    transform_file(root, "tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs", transform_roundtrip(), manifest)

    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


def refuse_forbidden_root(root: Path) -> None:
    forbidden = {
        SOURCE_ARTIFACT_MAIN_WORKTREE,
        SHARED_REPOSITORY_ROOT,
    }
    for path in forbidden:
        if root == path:
            fail(f"Refusing to modify author/main/shared worktree: {root}")


def assert_detached_clean_source(root: Path, *, allow_verified_hashes: bool) -> None:
    symbolic = run_git(root, "symbolic-ref", "-q", "--short", "HEAD", check=False)
    if symbolic.returncode == 0:
        fail(f"Refusing attached branch worktree '{symbolic.stdout.strip()}'; use a detached scratch worktree.")

    status = git(root, "status", "--porcelain=v1")
    if status.strip():
        fail("Refusing dirty scratch worktree before materialization:\n" + status)

    head = git(root, "rev-parse", "HEAD").strip()
    if head != ACCEPTED_SOURCE_COMMIT and not allow_verified_hashes:
        fail(
            f"HEAD {head} is not accepted source8f {ACCEPTED_SOURCE_COMMIT}. "
            "If and only if parent has verified the accepted compiler-file hashes, "
            "rerun with --accept-verified8f-file-hashes."
        )

    for relative, expected in PINNED_INPUT_SHA256.items():
        actual = sha256_file(root / relative)
        if actual != expected:
            fail(f"Source mismatch for {relative}: expected source8f {expected}, got {actual}")


def write_mode_source(
    root: Path,
    script_dir: Path,
    template_name: str,
    relative_out: str,
    mode: str,
    manifest: dict[str, object],
) -> None:
    template_path = script_dir / template_name
    template = template_path.read_text(encoding="utf-8")
    placeholder_count = template.count("__D1_MEASUREMENT_MODE__")
    if placeholder_count != 1:
        fail(f"{template_name}: expected exactly one mode placeholder, found {placeholder_count}")
    source = template.replace("__D1_MEASUREMENT_MODE__", mode)
    out_path = root / relative_out
    if out_path.exists():
        fail(f"Refusing to overwrite existing generated source {relative_out}")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(source.encode("utf-8"))
    expected = sha256_text(source)
    actual = sha256_file(out_path)
    if actual != expected:
        fail(f"Generated source hash mismatch for {relative_out}: expected {expected}, got {actual}")
    manifest["addedSources"].append(
        {
            "template": template_name,
            "templateSha256": sha256_text(template),
            "path": relative_out,
            "modePlaceholderCount": placeholder_count,
            "sha256": actual,
        }
    )


def transform_file(
    root: Path,
    relative: str,
    transform: Callable[[str, list[dict[str, object]]], str],
    manifest: dict[str, object],
) -> None:
    path = root / relative
    before = path.read_text(encoding="utf-8")
    before_hash = sha256_text(before)
    expected_before = PINNED_INPUT_SHA256[relative]
    if before_hash != expected_before:
        fail(f"{relative}: expected source8f hash {expected_before}, got {before_hash}")
    operations: list[dict[str, object]] = []
    after = transform(before, operations)
    after_hash = sha256_text(after)
    path.write_bytes(after.encode("utf-8"))
    actual_after_hash = sha256_file(path)
    if actual_after_hash != after_hash:
        fail(f"{relative}: post-write hash mismatch expected {after_hash}, got {actual_after_hash}")
    manifest["transformations"].append(
        {
            "path": relative,
            "beforeSha256": before_hash,
            "afterSha256": actual_after_hash,
            "operationCount": len(operations),
            "operations": operations,
        }
    )


def transform_binder(mode: str) -> Callable[[str, list[dict[str, object]]], str]:
    def apply(text: str, operations: list[dict[str, object]]) -> str:
        text = replace_exact(
            text,
            "Binder.Bind begin capture hook",
            """    public BoundModule Bind(ModuleNode module)
    {
        using var diagnosticContext = _diagnostics.EnterBindingContext();""",
            """    public BoundModule Bind(ModuleNode module)
    {
        using var d1BindingCapture = D1NominalPolicyCapture.BeginBind(module, _sourceIdentity);
        using var diagnosticContext = _diagnostics.EnterBindingContext();""",
            operations,
        )
        text = replace_exact(
            text,
            "Binder.Bind completed capture hook",
            """        return new BoundModule(
            module.Span,
            module.Name,
            functions,
            new Dictionary<SymbolId, Symbol>(_symbolsById));""",
            """        var boundModule = new BoundModule(
            module.Span,
            module.Name,
            functions,
            new Dictionary<SymbolId, Symbol>(_symbolsById));
        D1NominalPolicyCapture.CompleteBind(d1BindingCapture, boundModule, _diagnostics, _metadataBinder);
        return boundModule;""",
            operations,
        )
        text = replace_exact(
            text,
            "BCL resolution actual result capture",
            """        var result = binder.ResolveCall(receiverType, methodName, metaArgs);
        if (!result.IsResolved) return null;

        return result;""",
            """        var result = binder.ResolveCall(receiverType, methodName, metaArgs);
        D1NominalPolicyCapture.CaptureBclResolutionAttempt(
            callTarget,
            args,
            argumentNames,
            argumentModifiers,
            result);
        if (!result.IsResolved) return null;

        return result;""",
            operations,
        )
        text = replace_exact(
            text,
            "ValidateCallArguments signature and capture",
            """    private void ValidateCallArguments(
        IReadOnlyList<BoundExpression> arguments,
        OverloadResolutionResult resolution,
        Metadata.MetadataBinderResult? bclResolution)
    {
        var reported = new HashSet<(Parsing.TextSpan Span, string ParameterName, string TargetShape)>();""",
            """    private void ValidateCallArguments(
        IReadOnlyList<BoundExpression> arguments,
        OverloadResolutionResult resolution,
        Metadata.MetadataBinderResult? bclResolution,
        string callForm,
        Parsing.TextSpan callSpan,
        string callTarget)
    {
        D1NominalPolicyCapture.CaptureCallArgumentValidation(
            arguments,
            resolution,
            bclResolution,
            callForm,
            callSpan,
            callTarget);
        var reported = new HashSet<(Parsing.TextSpan Span, string ParameterName, string TargetShape)>();""",
            operations,
        )
        text = replace_exact(
            text,
            "statement call validation hook arguments",
            """        var bclResolution = resolution.Kind == OverloadResolutionKind.NotFound
            ? TryResolveBclCall(call.Target, args, call.ArgumentNames, call.ArgumentModifiers)
            : null;
        ValidateCallArguments(args, resolution, bclResolution);
        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(""",
            """        var bclResolution = resolution.Kind == OverloadResolutionKind.NotFound
            ? TryResolveBclCall(call.Target, args, call.ArgumentNames, call.ArgumentModifiers)
            : null;
        ValidateCallArguments(args, resolution, bclResolution, "statement", call.Span, call.Target);
        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(""",
            operations,
        )
        text = replace_exact(
            text,
            "expression call validation hook arguments",
            """        ValidateCallArguments(args, resolution, bclResolution);

        return new BoundCallExpression(""",
            """        ValidateCallArguments(args, resolution, bclResolution, "expression", callExpr.Span, callExpr.Target);

        return new BoundCallExpression(""",
            operations,
        )
        return text

    return apply


def transform_nullability(mode: str) -> Callable[[str, list[dict[str, object]]], str]:
    def apply(text: str, operations: list[dict[str, object]]) -> str:
        text = replace_exact(
            text,
            "shared predicate capture hook",
            """        return target switch
        {
            NominalBoundType nominal => CheckScalarStringTarget(source, nominal),
            // S6 — array-element STRING nullability. The array container's
            // own annotation is orthogonal (we only diagnose the element
            // mismatch); a possibly-null-elements source assigned to a
            // non-null-elements array target trips the same predicate.
            ArrayBoundType array => CheckArrayStringElementTarget(source, array),
            // S7 — whitelisted generic-instantiation STRING nullability
            // (Option<T>, List<T>, IList<T>, IEnumerable<T>,
            // IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>).
            // Container's own annotation is orthogonal — only the
            // position-0 type argument (payload / element) mismatch
            // matters, symmetric to the S6 array shape.
            GenericInstantiationBoundType generic => CheckGenericStringArgumentTarget(source, generic),
            _ => false,
        };""",
            """        var result = target switch
        {
            NominalBoundType nominal => CheckScalarStringTarget(source, nominal),
            // S6 — array-element STRING nullability. The array container's
            // own annotation is orthogonal (we only diagnose the element
            // mismatch); a possibly-null-elements source assigned to a
            // non-null-elements array target trips the same predicate.
            ArrayBoundType array => CheckArrayStringElementTarget(source, array),
            // S7 — whitelisted generic-instantiation STRING nullability
            // (Option<T>, List<T>, IList<T>, IEnumerable<T>,
            // IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>).
            // Container's own annotation is orthogonal — only the
            // position-0 type argument (payload / element) mismatch
            // matters, symmetric to the S6 array shape.
            GenericInstantiationBoundType generic => CheckGenericStringArgumentTarget(source, generic),
            _ => false,
        };
        D1NominalPolicyCapture.CapturePredicateAttempt(source, target, result);
        return result;""",
            operations,
        )
        if mode == "shadow-conservative":
            text = replace_exact(
                text,
                "conservative nominal carrier widening",
                """            return sourceNominal.NullableAnnotation == NullableAnnotation.Annotated;""",
                """            return sourceNominal.NullableAnnotation is NullableAnnotation.Annotated or NullableAnnotation.Oblivious;""",
                operations,
            )
        return text

    return apply


def transform_scope() -> Callable[[str, list[dict[str, object]]], str]:
    def apply(text: str, operations: list[dict[str, object]]) -> str:
        return replace_exact(
            text,
            "BindingDiagnosticPolicy nominal-only shadow override",
            """        if (context is not null)
        {
            var receiving = ReceivingRules.FirstOrDefault(candidate =>
                candidate.Code == code && candidate.Context == context);
            if (receiving is not null)
                return receiving.Policy;
        }""",
            """        if (context is not null)
        {
            if (D1NominalPolicyCapture.TryGetBindingPolicyOverride(code, context, out var d1Override))
                return d1Override;
            var receiving = ReceivingRules.FirstOrDefault(candidate =>
                candidate.Code == code && candidate.Context == context);
            if (receiving is not null)
                return receiving.Policy;
        }""",
            operations,
        )

    return apply


def transform_roundtrip() -> Callable[[str, list[dict[str, object]]], str]:
    def apply(text: str, operations: list[dict[str, object]]) -> str:
        return replace_exact(
            text,
            "RoundTripPipeline raw TRX archival hook",
            """            var result = await RunTestsAsync(workDir, config, noBuild, cancellationToken);
            _evidence?.TestAttempts.Add(new TestAttemptEvidence { Leg = leg, Attempt = attempt, Result = result });""",
            """            var result = await RunTestsAsync(workDir, config, noBuild, cancellationToken);
            D1RoundTripAttemptArchive.Archive(workDir, leg, attempt, result);
            _evidence?.TestAttempts.Add(new TestAttemptEvidence { Leg = leg, Attempt = attempt, Result = result });""",
            operations,
        )

    return apply


def replace_exact(
    text: str,
    label: str,
    old: str,
    new: str,
    operations: list[dict[str, object]],
) -> str:
    count = text.count(old)
    if count != 1:
        fail(f"{label}: expected exact source occurrence count 1, got {count}")
    replaced = text.replace(old, new, 1)
    new_count = replaced.count(new)
    if new_count != 1:
        fail(f"{label}: expected exact transformed occurrence count 1, got {new_count}")
    operations.append(
        {
            "label": label,
            "expectedOccurrenceCount": 1,
            "oldSha256": sha256_text(old),
            "newSha256": sha256_text(new),
        }
    )
    return replaced


def git(root: Path, *args: str) -> str:
    return run_git(root, *args, check=True).stdout


def run_git(root: Path, *args: str, check: bool) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if check and result.returncode != 0:
        fail(f"git {' '.join(args)} failed in {root}: {result.stderr.strip()}")
    return result


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def fail(message: str) -> None:
    raise SystemExit(message)


if __name__ == "__main__":
    sys.exit(main())
