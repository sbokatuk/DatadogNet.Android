#!/bin/sh
# Regenerates build/maven-checksums.txt: one pinned SHA-256 per Maven artifact any project in
# this repository resolves, at the versions currently declared.
#
# Run it after bumping DatadogNativeVersion (or any third-party artifact version), then diff the
# result - a hash that CHANGED for an unchanged version is exactly the event the pins exist to
# catch, and wants investigating rather than committing.
#
# Where the hashes come from: the repository's own published .sha256 sidecar when it serves one
# (Maven Central and Google's Maven both do), otherwise computed from a fresh download over
# HTTPS. That anchors trust at "what the publisher served on this date". For the com.datadoghq
# artifacts the anchor is stronger: Central also serves a detached PGP signature (.asc) for
# every file, so when gpg is available each one is downloaded and verified against Datadog's
# release signing key, pinned below by full fingerprint. Missing gpg is a loud skip; a bad or
# wrong-key signature is a hard failure.
set -eu

# Datadog's dd-sdk-android release signing key, pinned by FULL fingerprint so a keyserver that
# serves a different key with a colliding key id cannot satisfy the check. uid:
# "Datadog dd-sdk-android Packaging <package+dd-sdk-android@datadoghq.com>".
#
# To re-derive it: download any Datadog artifact's .asc from Central, e.g.
#   curl -O https://repo1.maven.org/maven2/com/datadoghq/dd-sdk-android-core/<v>/dd-sdk-android-core-<v>.aar.asc
# then `gpg --list-packets` it to read the issuer key id (9333D4EF32A49F0A), fetch that key with
# `gpg --keyserver keyserver.ubuntu.com --recv-keys 0x9333D4EF32A49F0A`, and read the full
# fingerprint from `gpg --fingerprint` - then confirm the fetched key actually verifies the .asc
# before pinning it.
DATADOG_PGP_FINGERPRINT="CAF18D4EC00CA4450C6725A59333D4EF32A49F0A"

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

# One keyring containing exactly the pinned key, so a signature by any other key cannot verify
# even before the fingerprint is compared. gpg being absent skips signature verification loudly
# rather than failing - the SHA-256 pins still anchor "what the publisher served today" - but
# gpg being present and the key being unfetchable is an error: this script runs at exactly the
# moments (version bumps, CI drift checks) where silently weakening the check would matter.
gpg_home=""
if command -v gpg >/dev/null 2>&1; then
    gpg_home="$work/gnupg"
    mkdir "$gpg_home"
    chmod 700 "$gpg_home"
    if ! gpg --homedir "$gpg_home" --quiet --batch \
             --keyserver hkps://keyserver.ubuntu.com \
             --recv-keys "$DATADOG_PGP_FINGERPRINT" </dev/null 2>/dev/null; then
        echo "error: could not fetch Datadog's signing key $DATADOG_PGP_FINGERPRINT from keyserver.ubuntu.com" >&2
        echo "       gpg is installed, so PGP verification is expected to run; retry, or check the keyserver." >&2
        exit 1
    fi
    echo "==> verifying com.datadoghq artifacts against PGP key $DATADOG_PGP_FINGERPRINT"
else
    echo "warning: gpg is not installed - SKIPPING PGP verification of the com.datadoghq artifacts." >&2
    echo "         The SHA-256 pins still anchor what the publisher served today; install gnupg to also" >&2
    echo "         verify that Datadog signed it." >&2
fi

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

    # For Datadog's artifacts, authenticate the actual bytes before pinning them: download the
    # file and its detached .asc and verify the signature against the pinned key. A signature
    # that fails - or that verifies under any other key - aborts the whole run, because pinning
    # an unauthenticated hash would launder it into every later build.
    verified=""
    if [ -n "$gpg_home" ] && [ "$group" = "com.datadoghq" ]; then
        [ -f "$work/$file" ] || curl -fsSL -o "$work/$file" "$url"
        curl -fsSL -o "$work/$file.asc" "$url.asc"
        if ! gpg --homedir "$gpg_home" --batch --status-fd 1 \
                 --verify "$work/$file.asc" "$work/$file" </dev/null 2>/dev/null \
                | grep -q "VALIDSIG.* $DATADOG_PGP_FINGERPRINT\$"; then
            echo "error: $file failed PGP verification against $DATADOG_PGP_FINGERPRINT - investigate before pinning anything" >&2
            exit 1
        fi
        verified=", PGP signature verified"
    fi

    if hash=$(curl -fsSL "$url.sha256" 2>/dev/null) && [ -n "$hash" ]; then
        # Some sidecars append the file name; keep the hex only.
        hash=$(printf '%s' "$hash" | tr -d '\r\n' | cut -d' ' -f1 | tr 'A-F' 'a-f')
        if [ -f "$work/$file" ]; then
            # The bytes were downloaded for signature verification, so require the published
            # sidecar to describe those same bytes - a repository serving an inconsistent pair
            # is exactly what must not be pinned.
            computed=$(shasum -a 256 "$work/$file" | cut -d' ' -f1)
            if [ "$computed" != "$hash" ]; then
                echo "error: $file: the published .sha256 ($hash) disagrees with the downloaded bytes ($computed)" >&2
                exit 1
            fi
        fi
        echo "    $file: pinned from the published .sha256$verified"
    else
        [ -f "$work/$file" ] || curl -fsSL -o "$work/$file" "$url"
        hash=$(shasum -a 256 "$work/$file" | cut -d' ' -f1)
        echo "    $file: no .sha256 sidecar - computed from a fresh download$verified"
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
