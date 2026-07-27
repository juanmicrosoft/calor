#!/usr/bin/env bash
# ============================================================================
# check-probe-pair.sh <pair-dir> [more pair-dirs ...]
#
# Fixture acceptance for WS5 probe pairs (loop plan D5.1, kickoff
# docs/plans/loop-m4b-ws5.md §3 PR 2). For each pair it verifies the
# asymmetry the probe is built on, per the pair's defect.json class:
#
#   all classes:
#     - the C# STARTER fixture builds green under the arm's toolkit
#       (NRT + EnforceCodeStyleInBuild) — the C# toolchain does not catch
#       the defect statically
#     - both REFERENCE solutions build green (calor under
#       --enforce-effects) and pass the full held-out suite (including the
#       defect probe test) and the smoke suite
#   W5-A / W5-C:
#     - the calor STARTER fixture FAILS to compile under --enforce-effects
#       with Calor0410 (build-blocking catch)
#   W5-B:
#     - the calor STARTER fixture compiles (contract violations are not
#       build-blocking) but the arm-shared smoke suite FAILS against it
#       with a ContractViolationException (runtime-guard catch), while the
#       same smoke suite PASSES against the C# starter fixture
#
# Exit 0 = every pair accepted; 1 = any check failed.
# ============================================================================
set -euo pipefail
# Pin test-runner output to English: the acceptance greps (`Passed:`,
# `ContractViolationException`) are locale-sensitive.
export DOTNET_CLI_UI_LANGUAGE=en

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

CALOR_DLL=""
for cfg in Release Debug; do
    c="$REPO_ROOT/src/Calor.Compiler/bin/$cfg/net10.0/calor.dll"
    [[ -f "$c" ]] && { CALOR_DLL="$c"; break; }
done
[[ -n "$CALOR_DLL" ]] || { echo "calor.dll not found — build src/Calor.Compiler first" >&2; exit 2; }

fail=0
say()  { printf '%s\n' "$*"; }
bad()  { printf 'FAIL: %s\n' "$*"; fail=1; }

# Builds a scratch workspace for one arm from a source dir, mirroring
# run-pair.sh materialize(): calor arm uses the CalorArm template, C# arm
# the inline NRT+style csproj. Prints the workspace path.
make_ws() {
    local src_dir="$1" arm="$2"
    local ws
    ws="$(mktemp -d "${TMPDIR:-/tmp}/w5-accept-XXXXXX")"
    mkdir -p "$ws/src"
    cp -R "$src_dir/." "$ws/src/"
    if [[ "$arm" == "calor" ]]; then
        sed "s|__REPO_ROOT__|$REPO_ROOT|g" \
            "$SCRIPT_DIR/templates/calor-arm/CalorArm.csproj.template" > "$ws/src/Src.csproj"
    else
        cat > "$ws/src/Src.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
EOF
    fi
    echo "$ws"
}

# Writes a test csproj (heldout- or smoke-shaped) referencing the ws build.
write_test_proj() {
    local dir="$1" ws="$2"
    cat > "$dir/Tests.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Src"><HintPath>$ws/src/bin/Debug/net10.0/Src.dll</HintPath></Reference>
    <Reference Include="Calor.Runtime" Condition="Exists('$ws/src/bin/Debug/net10.0/Calor.Runtime.dll')">
      <HintPath>$ws/src/bin/Debug/net10.0/Calor.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
EOF
}

# Runs a test suite (dir of .cs + arm shim) against a built workspace.
# The held-out suite carries the full-surface TestShim; the smoke suite
# carries its own starting-surface SmokeShim (so it compiles against the
# starter fixture from iteration zero). Returns the dotnet-test exit code.
run_suite() {
    local pair_dir="$1" ws="$2" log="$3" arm="$4" suite_dir="$5"
    local t
    t="$(mktemp -d "${TMPDIR:-/tmp}/w5-suite-XXXXXX")"
    cp "$pair_dir/$suite_dir"/*.cs "$t/"
    # smoke and the probe both compile against the STARTING surface via
    # SmokeShim; the held-out suite uses the full-surface TestShim.
    if [[ "$suite_dir" == "smoke" || "$suite_dir" == "tests/probe" ]]; then
        cp "$pair_dir/smoke/shims/SmokeShim.$arm.cs" "$t/SmokeShim.cs"
    else
        cp "$pair_dir/tests/shims/TestShim.$arm.cs" "$t/TestShim.cs"
    fi
    write_test_proj "$t" "$ws"
    local rc=0
    # Default verbosity, not -v q: the W5-B check greps the failure detail
    # for ContractViolationException, which quiet mode suppresses.
    dotnet test "$t/Tests.csproj" --nologo > "$log" 2>&1 || rc=$?
    rm -rf "$t"
    return $rc
}

check_pair() {
    local pair_dir="$1"
    local pair_id class
    pair_id="$(basename "$pair_dir")"
    [[ -f "$pair_dir/defect.json" ]] || { bad "$pair_id: no defect.json"; return; }
    class="$(jq -r '.class' "$pair_dir/defect.json")"
    say "── $pair_id ($class)"

    local scratch fails_before=$fail
    scratch="$(mktemp -d "${TMPDIR:-/tmp}/w5-check-XXXXXX")"

    # ---- calor STARTER fixture ------------------------------------------
    # Compile a COPY: the CLI writes .g.cs next to its inputs, and an
    # in-place compile both strands artifacts on interrupt and races
    # concurrent runs on the same pair (review of #810 nit b).
    mkdir -p "$scratch/calor-src"
    cp "$pair_dir"/calor/*.calr "$scratch/calor-src/"
    local calr_files=() f
    while IFS= read -r f; do calr_files+=(-i "$f"); done \
        < <(find "$scratch/calor-src" -name '*.calr' | sort)
    local starter_log="$scratch/calor-starter.log" starter_rc=0
    dotnet "$CALOR_DLL" "${calr_files[@]}" --enforce-effects \
        > "$starter_log" 2>&1 || starter_rc=$?

    case "$class" in
      W5-A|W5-C)
        if grep -q "error Calor0410" "$starter_log"; then
            say "  ok: calor starter blocked by Calor0410"
        else
            bad "$pair_id: calor starter did NOT hit Calor0410 (class $class)"
        fi
        ;;
      W5-B)
        if grep -q "error" "$starter_log"; then
            bad "$pair_id: calor starter must COMPILE for W5-B (runtime-guard channel); got errors"
        else
            # Build the defective calor workspace and expect the smoke suite
            # to fail with a contract violation.
            local wsb; wsb="$(make_ws "$pair_dir/calor" calor)"
            if ! dotnet build "$wsb/src/Src.csproj" --nologo -v q > "$scratch/b-build.log" 2>&1; then
                bad "$pair_id: calor starter MSBuild failed (see $scratch/b-build.log)"
            elif run_suite "$pair_dir" "$wsb" "$scratch/b-smoke.log" calor smoke; then
                bad "$pair_id: smoke suite PASSED against defective calor starter — runtime guard did not fire"
            elif grep -q "ContractViolationException" "$scratch/b-smoke.log"; then
                say "  ok: calor starter smoke fails with ContractViolationException"
            else
                bad "$pair_id: calor starter smoke failed but not via ContractViolationException"
            fi
            rm -rf "$wsb"
        fi
        ;;
      *) bad "$pair_id: unknown class '$class'";;
    esac

    # ---- C# STARTER fixture builds green + smoke behavior ---------------
    local wsc; wsc="$(make_ws "$pair_dir/csharp" csharp)"
    if ! dotnet build "$wsc/src/Src.csproj" --nologo -v q > "$scratch/cs-build.log" 2>&1; then
        bad "$pair_id: C# starter fixture does not build (toolkit caught it statically? see $scratch/cs-build.log)"
    else
        say "  ok: C# starter builds green under NRT+style"
        if run_suite "$pair_dir" "$wsc" "$scratch/cs-smoke.log" csharp smoke; then
            say "  ok: C# starter passes the arm-shared smoke suite"
        else
            bad "$pair_id: C# starter FAILS the smoke suite — smoke must not directly assert the defect"
        fi
        # fails-iff-present, presence side: the probe must FAIL against the
        # defective starter build (review of #810 finding 8's restructure).
        if run_suite "$pair_dir" "$wsc" "$scratch/cs-probe.log" csharp tests/probe; then
            bad "$pair_id: probe PASSED against the defective C# starter — it does not detect the defect"
        else
            say "  ok: probe fails against the defective C# starter"
        fi
    fi
    rm -rf "$wsc"

    # ---- REFERENCE solutions: build green, pass held-out + smoke --------
    local arm
    for arm in calor csharp; do
        local wsr; wsr="$(make_ws "$pair_dir/reference/$arm" "$arm")"
        if ! dotnet build "$wsr/src/Src.csproj" --nologo -v q > "$scratch/ref-$arm-build.log" 2>&1; then
            bad "$pair_id: $arm reference does not build"
        else
            if run_suite "$pair_dir" "$wsr" "$scratch/ref-$arm-ho.log" "$arm" tests; then
                say "  ok: $arm reference passes held-out"
            else
                bad "$pair_id: $arm reference FAILS held-out (see $scratch/ref-$arm-ho.log)"
            fi
            if run_suite "$pair_dir" "$wsr" "$scratch/ref-$arm-probe.log" "$arm" tests/probe; then
                say "  ok: $arm reference passes the defect probe"
            else
                bad "$pair_id: $arm reference FAILS the defect probe"
            fi
            if run_suite "$pair_dir" "$wsr" "$scratch/ref-$arm-smoke.log" "$arm" smoke; then
                say "  ok: $arm reference passes smoke"
            else
                bad "$pair_id: $arm reference FAILS smoke"
            fi
        fi
        rm -rf "$wsr"
    done

    # Keep the scratch dir when this pair failed a check — the FAIL messages
    # reference logs inside it (review of #810 nit a).
    if [[ $fail -eq $fails_before ]]; then
        rm -rf "$scratch"
    else
        say "  (logs kept in $scratch)"
    fi
}

[[ $# -ge 1 ]] || { echo "Usage: check-probe-pair.sh <pair-dir> [...]" >&2; exit 2; }
for p in "$@"; do
    check_pair "${p%/}"
done
exit $fail
