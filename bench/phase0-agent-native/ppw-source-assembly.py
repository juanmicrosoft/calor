#!/usr/bin/env python3
"""Compose explicitly partitioned task fragments only for compilation."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import sys
import uuid
import xml.etree.ElementTree as ET

MANIFEST = ".ppw-source-assembly.json"
OUTPUT = "obj/ppw-source/Program.calr"
SDK_OUTPUTS = ("$(GeneratedGlobalUsingsFile)", "$(GeneratedAssemblyInfoFile)",
               "$(TargetFrameworkMonikerAssemblyAttributesPath)")


def require(condition, message):
    if not condition:
        raise ValueError("source assembly: " + message)


def definition(pair):
    if "sourceAssembly" not in pair:
        return None
    value = pair["sourceAssembly"]
    if (isinstance(value, dict) and set(value) == {"parts"}
            and value["parts"] == ["dependency.calr.inc", "task.calr.inc"]):
        value = dict(value, editableParts=["task.calr.inc"])
    require(isinstance(value, dict) and set(value) == {"parts", "editableParts"},
            "parts and editableParts must be explicit")
    parts, editable = value["parts"], value["editableParts"]
    require(isinstance(parts, list) and len(parts) >= 2
            and all(isinstance(p, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*\.calr\.inc", p)
                    for p in parts) and len(set(parts)) == len(parts),
            "parts must be unique flat .calr.inc filenames in compilation order")
    require(isinstance(editable, list) and editable
            and all(isinstance(p, str) for p in editable)
            and len(set(editable)) == len(editable) and set(editable) < set(parts),
            "editableParts must be an explicit nonempty proper subset of parts")
    return value


def source_paths(pair, directory, editable_only=False):
    config = definition(pair)
    root = Path(directory)
    require(not root.is_symlink(), "linked source directory")
    if config is None:
        return sorted(root.rglob("*.calr"))
    names = config["editableParts"] if editable_only else config["parts"]
    paths = [root / name for name in names]
    for path in paths:
        require(path.is_file() and not path.is_symlink() and path.stat().st_nlink == 1,
                "missing or linked fragment: " + path.name)
    return paths


def check_fragments(pair, directory, immutable=None):
    config = definition(pair)
    require(config is not None, "fragment configuration required")
    root = Path(directory)
    require(not root.is_symlink(), "linked source directory")
    expected = set(config["parts"])
    actual = {
        p.relative_to(root).as_posix() for p in root.rglob("*")
        if not {"bin", "obj"} & set(p.relative_to(root).parts)
        and (str(p).endswith((".calr", ".calr.inc", ".cs")))
    }
    require(actual == expected, "source inventory differs from declared fragments")
    paths = source_paths(pair, root)
    for path in paths:
        require(path.read_bytes().endswith(b"\n"), "each fragment must end with a newline: " + path.name)
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
              for p in paths if p.name not in config["editableParts"]}
    if immutable is not None:
        require(hashes == immutable, "immutable dependency fragments changed")
    return hashes


def setup(pair, source):
    config = definition(pair)
    require(config is not None, "fragment configuration required")
    root = Path(source)
    require(not root.is_symlink(), "linked source directory")
    immutable = check_fragments(pair, root)
    manifest = root / MANIFEST
    require(not manifest.exists(), "workspace is already configured")
    project_path = root / "Src.csproj"
    project = ET.parse(project_path)
    declarations = [(parent, node) for parent in project.getroot().iter()
                    for node in parent if node.tag == "CalorCompile"]
    require(len(declarations) == 1 and declarations[0][1].get("Include") == "**/*.calr",
            "expected the generated Calor project template")
    declarations[0][1].set("Include", "$(MSBuildThisFileDirectory)" + OUTPUT)
    target = ET.SubElement(project.getroot(), "Target", Name="_PpwAssembleSources",
                           BeforeTargets="CompileCalorFiles")
    command = " ".join(shlex.quote(str(arg)) for arg in
                       (sys.executable, Path(__file__).resolve(), "compose", manifest.resolve()))
    ET.SubElement(target, "Exec", Command=command)
    reset = ET.SubElement(project.getroot(), "Target", Name="_PpwResetSdkSources",
                          BeforeTargets="GenerateGlobalUsings;CoreGenerateAssemblyInfo;"
                                        "GenerateTargetFrameworkMonikerAttribute")
    command = " ".join(shlex.quote(str(arg)) for arg in
                       (sys.executable, Path(__file__).resolve(), "reset-sdk", root.resolve()))
    command += " " + " ".join('"' + path + '"' for path in SDK_OUTPUTS)
    ET.SubElement(reset, "Exec", Command=command)
    authoritative = ET.SubElement(project.getroot(), "Target", Name="AddCalorGeneratedFilesToCompile",
                                  BeforeTargets="CoreCompile", DependsOnTargets="CompileCalorFiles",
                                  Condition="'@(CalorCompile)' != ''")
    items = ET.SubElement(authoritative, "ItemGroup")
    ET.SubElement(items, "_PpwUnexpectedCompile", Include="@(Compile)",
                  Exclude=";".join(SDK_OUTPUTS) + ";@(CalorGeneratedFiles)")
    ET.SubElement(authoritative, "Error", Condition="'@(_PpwUnexpectedCompile)' != ''",
                  Text="Unregistered C# input: @(_PpwUnexpectedCompile)")
    items = ET.SubElement(authoritative, "ItemGroup")
    ET.SubElement(items, "Compile", Include="@(CalorGeneratedFiles)")
    project.write(project_path, encoding="unicode")
    manifest.write_text(json.dumps({"sourceAssembly": config, "immutableSha256": immutable},
                                   indent=2) + "\n", encoding="utf-8")


def workspace_snapshot(source):
    root = Path(source)
    require(not root.is_symlink(), "linked source directory")
    manifest = root / MANIFEST
    require(not manifest.is_symlink(), "linked assembly manifest")
    config = json.loads(manifest.read_text(encoding="utf-8"))
    require(set(config) == {"sourceAssembly", "immutableSha256"}, "invalid workspace manifest")
    return check_fragments(config, root, config["immutableSha256"])


def compose(manifest):
    manifest = Path(manifest)
    root = manifest.parent
    workspace_snapshot(root)
    config = json.loads(manifest.read_text(encoding="utf-8"))
    content = b"\n".join(path.read_bytes() for path in source_paths(config, root))
    output = root / OUTPUT
    for path in (root / "obj", output.parent, output):
        require(not path.is_symlink(), "linked generated output")
    compiler_output = root / "obj/calor"
    require(not compiler_output.is_symlink(), "linked compiler output")
    if compiler_output.exists():
        shutil.rmtree(compiler_output)
    require(not output.exists() or output.stat().st_nlink == 1, "hard-linked generated output")
    output.parent.mkdir(parents=True, exist_ok=True)
    if not output.exists() or output.read_bytes() != content:
        staging = output.with_name(".Program-" + uuid.uuid4().hex)
        try:
            with staging.open("xb") as stream:
                stream.write(content)
            os.replace(staging, output)
        finally:
            if staging.exists():
                staging.unlink()
    return output


def reset_sdk_outputs(source, paths):
    root = Path(source).resolve()
    require(not (root / "obj").is_symlink(), "linked SDK output directory")
    for name in paths:
        if not name:
            continue
        path = Path(name)
        if not path.is_absolute():
            path = root / path
        require(path.resolve().is_relative_to(root / "obj") and path.suffix == ".cs",
                "SDK output must stay inside the source obj directory")
        require(not any(p.is_symlink() for p in (path, *path.parents) if p != root),
                "linked SDK generated input")
        if path.exists():
            path.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="operation", required=True)
    configure = sub.add_parser("setup")
    configure.add_argument("pair")
    configure.add_argument("source")
    generate = sub.add_parser("compose")
    generate.add_argument("manifest")
    reset = sub.add_parser("reset-sdk")
    reset.add_argument("source")
    reset.add_argument("paths", nargs="*")
    args = parser.parse_args()
    try:
        if args.operation == "setup":
            setup(json.loads(Path(args.pair).read_text(encoding="utf-8")), args.source)
        elif args.operation == "compose":
            compose(args.manifest)
        else:
            reset_sdk_outputs(args.source, args.paths)
        return 0
    except (OSError, ValueError, ET.ParseError) as exc:
        print(str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
