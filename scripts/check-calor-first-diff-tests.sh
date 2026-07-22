#!/usr/bin/env bash
# Behavioral tests for check-calor-first-diff.sh.
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
guard="$repo_root/scripts/check-calor-first-diff.sh"
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT

git init -q "$fixture"
cd "$fixture"
git config user.email calor-ci@example.invalid
git config user.name "Calor CI"
mkdir -p src
printf '§M{m001:Fixture}\n' > src/Source.calr
printf 'src/Generated.g.cs\n' > .calor-csharp-allowlist
printf 'class Existing {}\n' > src/Existing.cs
git add src/Source.calr .calor-csharp-allowlist src/Existing.cs
git commit -q -m baseline
base=$(git rev-parse HEAD)

expect_failure() {
  if bash "$guard" --working-tree "$base" >/dev/null 2>&1; then
    echo "Expected guard failure: $1" >&2
    exit 1
  fi
}

expect_success() {
  if ! bash "$guard" --working-tree "$base" >/dev/null; then
    echo "Expected guard success: $1" >&2
    exit 1
  fi
}

# New C# is blocked regardless of directory or whether a .calr ancestor exists.
printf 'class NewType {}\n' > src/NewType.cs
expect_failure "new C# beside .calr"
rm src/NewType.cs

# Root-level and new-directory C# are also blocked.
printf 'class RootType {}\n' > RootType.cs
expect_failure "root-level C#"
rm RootType.cs
mkdir -p new-directory
printf 'class NestedType {}\n' > new-directory/NestedType.cs
expect_failure "new-directory C#"
rm -rf new-directory

# Existing tracked C# is grandfathered for compiler/runtime maintenance.
printf 'class Existing { int Value; }\n' > src/Existing.cs
expect_success "existing C# maintenance"

# A generated path is permitted only when pre-approved on BASE; its suffix is
# not trusted by itself.
printf 'partial class Generated {}\n' > src/Generated.g.cs
expect_success "base-allowlisted generated C#"
rm src/Generated.g.cs

printf 'partial class Spoofed {}\n' > src/Spoofed.g.cs
expect_failure "spoofed generated suffix"
rm src/Spoofed.g.cs

# A PR cannot self-approve an exception by adding an allowlist in its diff.
printf 'src/Intentional.cs\n' > .calor-csharp-allowlist
printf 'class Intentional {}\n' > src/Intentional.cs
expect_failure "self-added allowlist"
rm .calor-csharp-allowlist src/Intentional.cs

# Removing the last Calor source while adding its C# replacement is blocked;
# the guard must inspect the merge base, not only the current filesystem.
rm src/Source.calr
printf 'class Replacement {}\n' > src/Replacement.cs
expect_failure "C# replacement for deleted .calr"

echo "Calor-first guard behavioral tests passed."
