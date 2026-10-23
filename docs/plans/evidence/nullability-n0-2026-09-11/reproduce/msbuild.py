import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
base = root / ".n0-evidence"
folder = base / "tmp" / "sdk"
folder.mkdir(parents=True, exist_ok=True)
project = folder / "N0.csproj"
project.write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <CalorTasksAssembly>{root}/src/Calor.Tasks/bin/Debug/net10.0/Calor.Tasks.dll</CalorTasksAssembly>
    <CalorSdkImportRuntime>false</CalorSdkImportRuntime>
    <CalorVerbose>true</CalorVerbose>
  </PropertyGroup>
  <Import Project="{root}/src/Calor.Sdk/Sdk/Sdk.props" />
  <Import Project="{root}/src/Calor.Sdk/Sdk/Sdk.targets" />
  <ItemGroup>
    <Reference Include="Calor.Runtime">
      <HintPath>{root}/src/Calor.Runtime/bin/Debug/net10.0/Calor.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
""")
environment = os.environ.copy()
environment.pop("CALOR_NO_TYPE_CHECK", None)
environment["TMPDIR"] = str(base / "tmp")
environment["CALOR_TELEMETRY"] = "0"
rows = []
for index, (identifier, flags, extra) in enumerate([
    ("bind-bcl-nullable", [], {}),
    ("bind-bcl-nullable", [], {}),
    ("return-bcl-nullable", [], {}),
    ("argument-bcl-expression", [], {}),
    ("active-duplicate", [], {}),
    ("active-duplicate", ["-p:CalorTypeCheck=false"], {}),
    ("active-duplicate", ["-p:CalorTranspileOnly=true"], {}),
    ("active-duplicate", ["-p:CalorPermissiveEffects=true", "-p:CalorVerify=true"], {}),
    ("active-duplicate", [], {"CALOR_NO_TYPE_CHECK": "1"}),
    ("bind-literal-nullable-target", [], {}),
    ("bind-literal-nullable-target", [], {"CALOR_NO_TYPE_CHECK": "1"}),
    ("bind-literal", [], {}),
    ("bind-literal", [], {}),
    ("bind-literal", ["-p:Nullable=disable"], {}),
    ("bind-literal", ["-p:CalorTypeCheck=false"], {}),
]):
    source = (base / "cases" / (identifier + ".calr")).read_text()
    (folder / "Input.calr").write_text(source)
    command = ["dotnet", "build", str(project), "--nologo", "-v:minimal", *flags]
    result = subprocess.run(command, cwd=root, env=environment | extra,
                            capture_output=True, text=True, timeout=180)
    log = result.stdout + result.stderr
    (base / f"sdk-{index}.log").write_text(log)
    cache_path = folder / "obj/Debug/net10.0/calor/.calor-build-state.json"
    rows.append({
        "index": index, "case": identifier,
        "command": [arg.replace(str(root), "<REPO>") for arg in command],
        "environmentOverrides": extra, "exit": result.returncode,
        "log": log.replace(str(root), "<REPO>"),
        "cache": json.loads(cache_path.read_text()) if cache_path.exists() else None,
    })
    print(index, identifier, result.returncode, flush=True)
(base / "sdk-matrix.json").write_text(json.dumps({
    "project": project.read_text().replace(str(root), "<REPO>"), "rows": rows
}, indent=2) + "\n")
