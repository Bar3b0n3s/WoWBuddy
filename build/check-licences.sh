#!/usr/bin/env bash
#
# Fails if any package in the dependency graph is not on the allow-list in
# build/allowed-packages.txt. See that file for why this is an allow-list.
#
# Run it the same way CI does:
#   ./build/check-licences.sh
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
allow_file="$repo_root/build/allowed-packages.txt"

# 'dotnet list package' cannot read a solution filter, and on non-Windows it cannot load the
# WPF project either, so the projects are enumerated directly. The UI project is excluded
# here; it adds no package references of its own beyond what its project references already
# bring in, and the Windows CI build is what proves it restores.
mapfile -t projects < <(find "$repo_root/src" "$repo_root/tools" "$repo_root/tests" \
    -name '*.csproj' -not -path '*/UI/*' | sort)

if [ ${#projects[@]} -eq 0 ]; then
    echo "No projects found. Check the paths in $0." >&2
    exit 1
fi

found=$(mktemp)
trap 'rm -f "$found"' EXIT

for project in "${projects[@]}"; do
    echo "Scanning $(basename "$project")"
    # Package lines look like:  "   > Serilog   4.1.0   4.1.0"
    dotnet list "$project" package --include-transitive \
        | awk '/^[[:space:]]*>/ { print $2 }' >> "$found"
done

sort -u -o "$found" "$found"

allowed=$(mktemp)
trap 'rm -f "$found" "$allowed"' EXIT
grep -vE '^\s*(#|$)' "$allow_file" | tr -d '\r' | sort -u > "$allowed"

echo
echo "Packages in the graph:"
sed 's/^/  /' "$found"

unknown=$(comm -23 "$found" "$allowed")

if [ -n "$unknown" ]; then
    echo
    echo "::error::Packages not on the licence allow-list:"
    echo "$unknown" | sed 's/^/  /'
    echo
    echo "Each must be checked to be MIT, BSD, Apache-2.0 or zlib, then added to"
    echo "build/allowed-packages.txt with its licence. Copyleft packages cannot be linked"
    echo "into an MIT program: see docs/legal-and-licensing.md."
    exit 1
fi

echo
echo "All packages are on the licence allow-list."
