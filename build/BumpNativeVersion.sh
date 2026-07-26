#!/bin/sh
# Bumps the bound dd-sdk-android line and runs every scripted step of the upgrade in order,
# stopping at the first failure so nothing later runs against a half-updated tree.
#
#   ./build/BumpNativeVersion.sh 3.13.0
#
# What it does: DatadogNativeVersion := <new-version> and DatadogBindingRevision := 1 in
# Directory.Build.props; re-pins every artifact hash (UpdateMavenChecksums.sh, which also
# PGP-verifies the Datadog downloads when gpg is present); regenerates the consumer R8
# keep-rules from the new .aars (generate-r8-rules.sh); re-checks every module's declared
# dependencies against its Gradle metadata (verify-transitive-deps.py); rewrites the README's
# pinned versions; and scaffolds docs/release-notes/<package version>.md. What it deliberately
# does not do is printed at the end - the steps that are judgement rather than mechanics.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
props="$root/Directory.Build.props"
readme="$root/README.md"

new_native="${1:?usage: BumpNativeVersion.sh <dd-sdk-android version, e.g. 3.13.0>}"
if ! printf '%s' "$new_native" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "error: '$new_native' does not look like a dd-sdk-android version (expected e.g. 3.13.0)" >&2
    exit 1
fi

prop() {
    sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$props" | head -1
}

old_native="$(prop DatadogNativeVersion)"
old_revision="$(prop DatadogBindingRevision)"
old_version="$old_native.$old_revision"
new_version="$new_native.1"

if [ "$old_native" = "$new_native" ]; then
    echo "error: DatadogNativeVersion is already $new_native" >&2
    exit 1
fi

echo "==> $old_version -> $new_version (dd-sdk-android $old_native -> $new_native)"

# 1. The properties. Revision resets to 1: the fourth component counts binding changes within
#    one native line, so a new line starts it over.
sed -e "s|<DatadogNativeVersion>$old_native</DatadogNativeVersion>|<DatadogNativeVersion>$new_native</DatadogNativeVersion>|" \
    -e "s|<DatadogBindingRevision>$old_revision</DatadogBindingRevision>|<DatadogBindingRevision>1</DatadogBindingRevision>|" \
    "$props" > "$props.tmp" && mv "$props.tmp" "$props"

[ "$(prop DatadogNativeVersion)" = "$new_native" ] || { echo "error: failed to rewrite DatadogNativeVersion" >&2; exit 1; }
[ "$(prop DatadogBindingRevision)" = "1" ] || { echo "error: failed to reset DatadogBindingRevision" >&2; exit 1; }

# 2. Pins before rules: generate-r8-rules.sh refuses to read an .aar whose hash is not pinned.
echo "==> re-pinning artifact hashes"
"$root/build/UpdateMavenChecksums.sh"

echo "==> regenerating R8 keep-rules from the $new_native .aars"
"$root/build/generate-r8-rules.sh"

echo "==> verifying declared dependencies against upstream's .module metadata"
python3 "$root/build/verify-transitive-deps.py"

# 3. The README's pinned versions: the PackageReference snippets and the device-check example,
#    which CheckReadmeVersions.sh enforces, plus the native version in the badge and in the
#    how-it-works diagram. Prose that explains the version *scheme* is deliberately left alone.
echo "==> rewriting README versions"
sed -e "s|\(Include=\"DatadogNet[^\"]*\" *Version=\"\)$old_version\"|\1$new_version\"|g" \
    -e "s|run-emulator-tests\.sh $old_version|run-emulator-tests.sh $new_version|g" \
    -e "/^\[!\[dd-sdk-android /s|$old_native|$new_native|g" \
    -e "s|dd-sdk-android-\*:$old_native|dd-sdk-android-*:$new_native|g" \
    "$readme" > "$readme.tmp" && mv "$readme.tmp" "$readme"
"$root/build/CheckReadmeVersions.sh"

# 4. Release notes scaffold, so the release workflow finds curated notes rather than falling
#    back to raw commit subjects - and so writing them is a fill-in rather than a blank page.
notes="$root/docs/release-notes/$new_version.md"
if [ -f "$notes" ]; then
    echo "==> $notes already exists, leaving it alone"
else
    cat > "$notes" <<EOF
## What's changed

First release bound against
[dd-sdk-android $new_native](https://github.com/DataDog/dd-sdk-android/releases/tag/$new_native)
(previously $old_native). Package ids, namespaces and the binding surface are unchanged unless
noted below.

<!-- Summarise what $new_native brings, from upstream's release notes, and anything the binding
     had to change to follow it. Delete sections that do not apply. -->

## Upgrading from $old_version

Nothing to change.
EOF
    echo "==> scaffolded $notes"
fi

cat <<EOF

==> done. The steps that are judgement, not mechanics - in order:

  1. Review the diffs this script made. In build/maven-checksums.txt only artifacts whose
     version changed may have a changed hash; anything else is the event the pins exist to catch.
  2. Re-check each Transforms/Metadata.xml: a removal rule upstream has fixed lingers as a
     BG8A00 warning, not an error. Re-check the @(AndroidIgnoredJavaDependency) list in
     src/Datadog.Binding.props against verify-transitive-deps.py's warnings.
  3. Review the regenerated src/*/buildTransitive/*.pro for upstream rule changes, and extend
     the curated keeps in build/generate-r8-rules.sh if $new_native added entry points.
  4. ./build/BuildNugets.sh && dotnet test tests/DatadogNet.Android.PackageTests, then the
     emulator suite (see README, "Building locally").
  5. Write docs/release-notes/$new_version.md properly, and update any README prose about
     features $new_native added or removed.
  6. Commit, PR, and tag v$new_version once merged - the release workflow refuses a tag whose
     native version disagrees with Directory.Build.props, which after this script it cannot.
EOF
