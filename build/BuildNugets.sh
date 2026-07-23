#!/bin/sh

set -e

# Builds and packs every Datadog binding package listed in build/packages.tsv.
#
# Usage:
#   ./BuildNugets.sh                 # version from Directory.Build.props
#   ./BuildNugets.sh 2.26.3.1-rc.1   # explicit package version
#   ./BuildNugets.sh 2.27.0.1 2.27.0 # ...and bind a different dd-sdk-android line
#
# The second argument selects which native Datadog version to bind. Nothing needs fetching first:
# the .aar files are resolved from Maven Central by @(AndroidMavenLibrary) and cached under
# ~/.cache/dotnet-android/MavenCacheDirectory.
#
# Packages are written to ../artifacts.
#
# Each .NET SDK's Android workload ships reference packs for only two target frameworks - the .NET
# 9 band covers net8/net9, the .NET 10 band covers net9/net10 - so this runs two passes and merges
# them. The repository's global.json pins the .NET 9 SDK, so the second pass is invoked from a
# scratch directory carrying its own global.json, since the SDK is resolved from the working
# directory.

cd "$(dirname "$0")"

VERSION="$1"
NATIVE_VERSION="$2"
ROOT="$(cd .. && pwd)"
OUTPUT="$ROOT/artifacts"

PASS1_BAND="net9"
PASS2_BAND="net10"
PASS2_SDK="10.0.100"

# Read in dependency order from the manifest rather than repeated here. Nothing requires the order -
# ProjectReference means MSBuild builds each package's dependencies on demand - but it keeps the log
# readable and means a failure in a base package is reported before the packages built on top of it
# repeat the same error.
PACKAGES=$(grep -v '^#' packages.tsv | grep -v '^[[:space:]]*$' | cut -f1)

if [ -z "$PACKAGES" ]; then
    echo "error: no packages found in build/packages.tsv" >&2
    exit 1
fi

VERSION_ARG=""
if [ -n "$VERSION" ]; then
    # Validated before being interpolated into MSBuild arguments and package file names.
    case "$VERSION" in
        *[!A-Za-z0-9.+_-]*)
            echo "error: invalid version '$VERSION'" >&2
            exit 1
            ;;
    esac
    VERSION_ARG="-p:Version=$VERSION"
fi

NATIVE_ARG=""
if [ -n "$NATIVE_VERSION" ]; then
    case "$NATIVE_VERSION" in
        *[!A-Za-z0-9.+_-]*)
            echo "error: invalid native version '$NATIVE_VERSION'" >&2
            exit 1
            ;;
    esac
    NATIVE_ARG="-p:DatadogNativeVersion=$NATIVE_VERSION"
fi

# NuGet.config declares ./artifacts as a package source, and restore fails outright with NU1301 if
# a local source directory is missing - before anything here has had a chance to create it as an
# output directory. A fresh clone only has it because an empty .gitkeep is committed, so make the
# build independent of that surviving.
mkdir -p "$OUTPUT"

PASS1_DIR="$OUTPUT/.net9-pass"
PASS2_DIR="$OUTPUT/.net10-pass"
rm -rf "$PASS1_DIR" "$PASS2_DIR"

SDK10_DIR="$(mktemp -d)"
trap 'rm -rf "$SDK10_DIR"' EXIT
cat > "$SDK10_DIR/global.json" <<EOF
{ "sdk": { "version": "$PASS2_SDK", "rollForward": "latestFeature" } }
EOF

for package in $PACKAGES; do
    project="$ROOT/src/DatadogNet.$package.Android/DatadogNet.$package.Android.csproj"

    if [ ! -f "$project" ]; then
        echo "error: $project does not exist, but build/packages.tsv lists $package" >&2
        exit 1
    fi

    echo "==> packing DatadogNet.$package.Android ($PASS1_BAND band)"
    dotnet pack "$project" \
        -c Release \
        -p:DatadogSdkBand="$PASS1_BAND" \
        $VERSION_ARG $NATIVE_ARG \
        -o "$PASS1_DIR"

    echo "==> packing DatadogNet.$package.Android ($PASS2_BAND band)"
    (cd "$SDK10_DIR" && dotnet pack "$project" \
        -c Release \
        -p:DatadogSdkBand="$PASS2_BAND" \
        $VERSION_ARG $NATIVE_ARG \
        -o "$PASS2_DIR")
done

echo "==> merging target frameworks"
python3 "$ROOT/build/merge-packages.py" "$PASS1_DIR" "$PASS2_DIR" "$OUTPUT"

rm -rf "$PASS1_DIR" "$PASS2_DIR"
