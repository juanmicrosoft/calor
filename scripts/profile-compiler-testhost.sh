#!/usr/bin/env bash
# VSTest's DotnetHostPath hook profiles the test host, not MSBuild or the test driver.
set -euo pipefail
test -n "${CALOR_PROFILE_DOTNET:?Set CALOR_PROFILE_DOTNET to the real dotnet executable}"
test -d "${CALOR_PROFILE_OUTPUT:?Set CALOR_PROFILE_OUTPUT to an existing output directory}"
exec heaptrack --record-only --output "$CALOR_PROFILE_OUTPUT/heaptrack.testhost.$$" \
  "$CALOR_PROFILE_DOTNET" "$@"
