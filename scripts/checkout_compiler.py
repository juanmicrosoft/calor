#!/usr/bin/env python3
"""Checkout-pinned compiler and fixture discovery for Tier 1 and Tier 2 (#1241).

Tier scripts run `dotnet src/Calor.Compiler/bin/Release/<tfm>/calor.dll` built
from this checkout; a missing or stale build is an error, never a fallback to
an installed `calor`. Fixtures come from `git ls-files`, so untracked bin/ and
obj/ copies are never selected.
"""

from __future__ import annotations

import hashlib
import os
import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
COMPILER_PROJECT = Path("src/Calor.Compiler")
# Build inputs: every non-ignored file (any type, tracked or not) under the
# compiler and runtime projects, plus the root build files. A build older
# than any of them, or a deleted tracked input, is stale. Freshness is
# judged by modification time, not by a content fingerprint.
SOURCE_DIRS = ("src/Calor.Compiler", "src/Calor.Runtime")
ROOT_BUILD_FILES = ("Directory.Build.props", "Directory.Build.targets",
                    "Directory.Packages.props", "global.json", "NuGet.config")
BUILD_OUTPUT_PARTS = frozenset({"bin", "obj"})


class CompilerResolutionError(RuntimeError):
    """The checkout-built compiler cannot be used. Never falls back."""


@dataclass(frozen=True)
class PinnedCompiler:
    dll: Path
    sha256: str
    head: str
    dirty: bool

    @property
    def command(self) -> list[str]:
        return ["dotnet", str(self.dll)]

    def provenance(self) -> dict:
        return {"compiler": "checkout-built calor.dll, never an installed tool",
                "dll": str(self.dll.relative_to(self.dll.parents[5])),
                "dllSha256": self.sha256, "head": self.head,
                "worktreeDirty": self.dirty}


def _target_framework(repo_root: Path) -> str:
    props = (repo_root / "Directory.Build.props").read_text(encoding="utf-8")
    m = re.search(r"<TargetFramework>([^<]+)</TargetFramework>", props)
    if not m:
        raise CompilerResolutionError(
            "Directory.Build.props declares no <TargetFramework>")
    return m.group(1).strip()


def _git(repo_root: Path, *args: str) -> str:
    cp = subprocess.run(["git", *args], cwd=repo_root, capture_output=True,
                        text=True)
    if cp.returncode != 0:
        raise CompilerResolutionError(
            f"git {' '.join(args)} failed: {cp.stderr.strip()}")
    return cp.stdout


def resolve(repo_root: Path = REPO_ROOT) -> PinnedCompiler:
    repo_root = repo_root.resolve()
    dll = (repo_root / COMPILER_PROJECT / "bin" / "Release"
           / _target_framework(repo_root) / "calor.dll")
    if not dll.is_file():
        raise CompilerResolutionError(
            f"checkout-built compiler not found: {dll}. Run "
            "`dotnet build src/Calor.Compiler -c Release` first. An installed "
            "`calor` tool is never used instead.")
    built = dll.stat().st_mtime
    # Files a project links from elsewhere (EmbeddedResource Include="..\..").
    linked = [os.path.normpath(f"{d}/{m}".replace("\\", "/"))
              for d in SOURCE_DIRS for p in (repo_root / d).glob("*.csproj")
              for m in re.findall(r'Include="(\.\.[^"]+)"', p.read_text())]
    inputs = (*SOURCE_DIRS, *ROOT_BUILD_FILES, *linked)
    missing = [m for m in linked if not (repo_root / m).exists()]
    files = _git(repo_root, "ls-files", "-z", "-co", "--exclude-standard",
                 "--", *inputs).split("\0")
    # Deleting or adding a file (even in a commit) bumps its directory.
    dirs = {str(Path(f).parent) for f in files
            if f.startswith(tuple(d + "/" for d in SOURCE_DIRS)) or f in linked}
    stale = missing + [f for f in [*files, *dirs, *SOURCE_DIRS] if f
                       and (repo_root / f).exists()
                       and (repo_root / f).stat().st_mtime > built]
    if stale:
        raise CompilerResolutionError(
            f"checkout-built compiler {dll} is older than {len(stale)} build "
            f"input(s) (changed, added, or deleted), e.g. {stale[0]}. Rebuild "
            "with `dotnet build src/Calor.Compiler -c Release`.")
    head = _git(repo_root, "rev-parse", "HEAD").strip()
    dirty = bool(_git(repo_root, "status", "--porcelain", "--untracked-files=no")
                 .strip())
    return PinnedCompiler(dll=dll, sha256=hashlib.sha256(dll.read_bytes())
                          .hexdigest(), head=head, dirty=dirty)


def is_build_output(rel: str) -> bool:
    return any(part in BUILD_OUTPUT_PARTS for part in Path(rel).parts)


def tracked_calr(repo_root: Path, roots: list[str]) -> list[str]:
    """Tracked `.calr` files under `roots`, repo-relative, POSIX, sorted.

    A tracked file inside a `bin/` or `obj/` directory is an error, not a
    silent exclusion: it would be a committed build output.
    """
    out = _git(repo_root, "ls-files", "-z", "--",
               *[f"{r.rstrip('/')}/*.calr" for r in roots]).split("\0")
    files = sorted(f for f in out if f)
    leaked = [f for f in files if is_build_output(f)]
    if leaked:
        raise CompilerResolutionError(
            f"tracked .calr under a build-output directory: {leaked[0]}")
    return files


def copy_tracked(repo_root: Path, files: list[str], dest: Path) -> None:
    for rel in files:
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(repo_root / rel, target)


def untracked_calr_count(repo_root: Path, roots: list[str]) -> dict:
    out = _git(repo_root, "ls-files", "-z", "-o", "--",
               *[f"{r.rstrip('/')}/*.calr" for r in roots]).split("\0")
    build = sum(1 for f in out if f and is_build_output(f))
    return {"buildOutput": build,
            "otherUntracked": sum(1 for f in out if f) - build}
