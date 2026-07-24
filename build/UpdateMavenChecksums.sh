#!/bin/sh
# Regenerates build/maven-checksums.txt: one pinned SHA-256 per Maven artifact any project in
# this repository resolves, at the versions currently declared.
#
# Run it after bumping DatadogNativeVersion (or any third-party artifact version), then diff the
# result - a hash that CHANGED for an unchanged version is exactly the event the pins exist to
# catch, and wants investigating rather than committing.
#
# Where the hashes come from: the repository's own published .sha256 sidecar when it serves one
# (Maven Central does), otherwise computed from a fresh download over HTTPS (Google's Maven does
# not serve .sha256). Both anchor trust at "what the publisher served on this date", which is the
# strongest statement available - upstream signs nothing this build can check offline.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/build/maven-checksums.txt"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Every @(DatadogMavenArtifact) row, across every project, with the ItemDefinitionGroup defaults
# applied - which is why this asks MSBuild instead of grepping csproj files.
artifacts="$work/artifacts.tsv"
: > "$artifacts"
for project in "$root"/src/*/*.csproj; do
    dotnet msbuild "$project" -getItem:DatadogMavenArtifact 2>/dev/null | python3 -c "
import json, sys
try:
    items = json.load(sys.stdin)['Items'].get('DatadogMavenArtifact', [])
except Exception:
    items = []
for item in items:
    group, artifact = item['Identity'].split(':', 1)
    print('\t'.join([group, artifact, item['Version'], item.get('Packaging', 'aar'), item.get('Repository', 'Central')]))
" >> "$artifacts"
done

sort -u "$artifacts" -o "$artifacts"
count=$(wc -l < "$artifacts" | tr -d ' ')
if [ "$count" -eq 0 ]; then
    echo "error: no DatadogMavenArtifact rows found - is the .NET SDK installed?" >&2
    exit 1
fi
echo "==> pinning $count artifacts"

{
    echo "# SHA-256 pins for every Maven artifact this repository resolves."
    echo "#"
    echo "# Verified by the VerifyDatadogMavenArtifactHashes target in src/Datadog.Binding.props on"
    echo "# every build, on both resolution paths - @(AndroidMavenLibrary) and the direct-download"
    echo "# fallback. Regenerate with build/UpdateMavenChecksums.sh after a version bump; a hash"
    echo "# that changes for an UNCHANGED version is the tampering event these pins exist to catch."
    echo "#"
    echo "# <file name> <sha256>"
} > "$out"

while IFS="$(printf '\t')" read -r group artifact version packaging repository; do
    file="$artifact-$version.$packaging"
    grouppath=$(printf '%s' "$group" | tr '.' '/')
    if [ "$repository" = "Google" ]; then
        base="https://dl.google.com/dl/android/maven2"
    else
        base="https://repo1.maven.org/maven2"
    fi
    url="$base/$grouppath/$artifact/$version/$file"

    if hash=$(curl -fsSL "$url.sha256" 2>/dev/null) && [ -n "$hash" ]; then
        # Some sidecars append the file name; keep the hex only.
        hash=$(printf '%s' "$hash" | tr -d '\r\n' | cut -d' ' -f1 | tr 'A-F' 'a-f')
        echo "    $file: pinned from the published .sha256"
    else
        curl -fsSL -o "$work/$file" "$url"
        hash=$(shasum -a 256 "$work/$file" | cut -d' ' -f1)
        echo "    $file: no .sha256 sidecar - computed from a fresh download"
    fi

    case "$hash" in
        *[!0-9a-f]*|'')
            echo "error: '$hash' does not look like a SHA-256 for $file" >&2
            exit 1
            ;;
    esac

    printf '%s %s\n' "$file" "$hash" >> "$out"
done < "$artifacts"

sort_body="$(grep -v '^#' "$out" | sort)"
{ grep '^#' "$out"; printf '%s\n' "$sort_body"; } > "$out.tmp" && mv "$out.tmp" "$out"

echo "==> wrote $(grep -vc '^#' "$out") pins to $out"
