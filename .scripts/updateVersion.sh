#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <newVersion> <path/to/project.csproj>" >&2
  exit 2
fi

NEW_VERSION="$1"
CSPROJ_PATH="$2"

if [[ ! -f "$CSPROJ_PATH" ]]; then
  echo "Error: csproj file not found: $CSPROJ_PATH" >&2
  exit 1
fi

# If <Version> exists, replace it. Otherwise, insert into the first <PropertyGroup>.
if grep -qE '<Version>[^<]*</Version>' "$CSPROJ_PATH"; then
  perl -0777 -i -pe "s|<Version>[^<]*</Version>|<Version>${NEW_VERSION}</Version>|" "$CSPROJ_PATH"
else
  perl -0777 -i -pe "s|(<PropertyGroup[^>]*>\\s*)|\\1  <Version>${NEW_VERSION}</Version>\\n|s" "$CSPROJ_PATH"
fi

echo "Updated $CSPROJ_PATH to Version=$NEW_VERSION"