#!/usr/bin/env python3
"""Checks the declared Java dependency coverage against upstream's Gradle module metadata.

This automates the sharpest manual steps of an upgrade (README, "Upgrading the Datadog SDK"):
re-deriving each module's real runtime dependencies from its `.module` file - the `.pom` is
polluted by Gradle's flattened test-fixtures variant - and re-checking that the
@(AndroidIgnoredJavaDependency) list still matches that pollution. Both degrade silently when
done by eye: a dependency upstream adds is a runtime crash in somebody's app, and an ignore
entry upstream stops needing is a place a real missing dependency can hide.

For every binding project it:
  1. evaluates the project (dotnet msbuild -getItem) for its Datadog artifact, its declared
     JavaArtifact coverage, its embedded artifacts and its ignore list;
  2. fetches the artifact's `.module` and reads the releaseVariantReleaseRuntimePublication
     dependencies - the truth - and the `.pom`'s dependencies - the pollution;
  3. requires every `.module` runtime dependency to be covered by a sibling binding, an embedded
     artifact, a NuGet binding package (via the Maven->NuGet map below), or an ignore entry;
  4. requires every ignore entry that names a test-fixture library to still appear in some
     `.pom`, and flags the ones that no longer do.

Exit code 1 on uncovered dependencies or an unmappable coordinate; stale-ignore findings are
warnings, because removing them is judgement rather than mechanics.
"""

from __future__ import annotations

import json
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Maven coordinate -> NuGet binding package. The AndroidX family follows a convention and is
# transformed programmatically; everything else is listed. An unmapped coordinate fails the run
# with instructions, deliberately - guessing here would defeat the point.
MAVEN_TO_NUGET = {
    "org.jetbrains.kotlin:kotlin-stdlib": "Xamarin.Kotlin.StdLib",
    "org.jetbrains.kotlin:kotlin-stdlib-jdk8": "Xamarin.Kotlin.StdLib",
    "org.jetbrains.kotlinx:kotlinx-coroutines-core": "Xamarin.KotlinX.Coroutines.Core",
    "org.jetbrains.kotlinx:kotlinx-coroutines-android": "Xamarin.KotlinX.Coroutines.Android",
    "com.squareup.okhttp3:okhttp": "Square.OkHttp3",
    "com.squareup.okio:okio": "Square.OkIO",
    "com.google.code.gson:gson": "GoogleGson",
    "com.google.android.material:material": "Xamarin.Google.Android.Material",
}


def androidx_to_nuget(group: str, artifact: str) -> str | None:
    if not group.startswith("androidx."):
        return None
    # androidx.navigation:navigation-fragment -> Xamarin.AndroidX.Navigation.Fragment;
    # androidx.compose.ui:ui-tooling -> Xamarin.AndroidX.Compose.UI.Tooling. The artifact
    # usually repeats the group's last segment; drop the repeat. Compared case-insensitively
    # against the declared PackageReferences, since NuGet ids are (RecyclerView vs Recyclerview).
    group_parts = group[len("androidx."):].split(".")
    artifact_parts = artifact.split("-")
    if artifact_parts[0] == group_parts[-1]:
        artifact_parts = artifact_parts[1:]
    return "Xamarin.AndroidX." + ".".join(p.capitalize() for p in group_parts + artifact_parts)


def evaluate(project: Path) -> dict:
    result = subprocess.run(
        ["dotnet", "msbuild", str(project),
         "-getItem:ProjectReference,PackageReference,AndroidIgnoredJavaDependency,DatadogMavenArtifact",
         "-getProperty:DatadogArtifact,DatadogNativeVersion"],
        capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def fetch(url: str) -> bytes | None:
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def module_runtime_deps(artifact: str, version: str) -> list[tuple[str, str, str]]:
    url = (f"https://repo1.maven.org/maven2/com/datadoghq/{artifact}/{version}/"
           f"{artifact}-{version}.module")
    raw = fetch(url)
    if raw is None:
        raise SystemExit(f"error: no .module for {artifact} {version} at {url}")
    data = json.loads(raw)
    for variant in data.get("variants", []):
        if variant.get("name") == "releaseVariantReleaseRuntimePublication":
            # Platform/BOM entries are Gradle dependency management, not artifacts an app needs
            # on its classpath - androidx.compose:compose-bom, notably.
            return [(d["group"], d["module"], d.get("version", {}).get("requires", ""))
                    for d in variant.get("dependencies", [])
                    if d.get("attributes", {}).get("org.gradle.category") != "platform"]
    raise SystemExit(f"error: {artifact} {version} has no releaseVariantReleaseRuntimePublication variant")


def pom_deps(artifact: str, version: str) -> set[str]:
    url = (f"https://repo1.maven.org/maven2/com/datadoghq/{artifact}/{version}/"
           f"{artifact}-{version}.pom")
    raw = fetch(url)
    if raw is None:
        return set()
    ns = {"m": "http://maven.apache.org/POM/4.0.0"}
    tree = ET.fromstring(raw)
    out = set()
    for dep in tree.findall(".//m:dependencies/m:dependency", ns):
        group = dep.findtext("m:groupId", "", ns)
        art = dep.findtext("m:artifactId", "", ns)
        out.add(f"{group}:{art}")
    return out


def main() -> int:
    projects = sorted(ROOT.glob("src/DatadogNet.*.Android/DatadogNet.*.Android.csproj"))
    failures: list[str] = []
    warnings: list[str] = []
    all_pom_deps: set[str] = set()
    all_ignores: set[str] = set()

    for project in projects:
        info = evaluate(project)
        artifact = info["Properties"]["DatadogArtifact"]
        version = info["Properties"]["DatadogNativeVersion"]
        items = info["Items"]

        declared: set[str] = set()          # group:artifact covered by a reference
        nuget_ids = set()
        for item in items.get("ProjectReference", []):
            java = item.get("JavaArtifact", "")
            if java:
                declared.add(":".join(java.split(":")[:2]))
        for item in items.get("PackageReference", []):
            nuget_ids.add(item["Identity"])
        for item in items.get("DatadogMavenArtifact", []):
            declared.add(item["Identity"])
        ignores = {":".join(i["Identity"].split(":")[:2]) for i in items.get("AndroidIgnoredJavaDependency", [])}
        all_ignores |= {i["Identity"] for i in items.get("AndroidIgnoredJavaDependency", [])}

        deps = module_runtime_deps(artifact, version)
        all_pom_deps |= pom_deps(artifact, version)

        for group, module, required in deps:
            coordinate = f"{group}:{module}"
            if coordinate in declared or coordinate in ignores:
                continue
            expected_nuget = MAVEN_TO_NUGET.get(coordinate) or androidx_to_nuget(group, module)
            nuget_ids_folded = {n.casefold() for n in nuget_ids}
            if expected_nuget is None:
                failures.append(
                    f"{project.parent.name}: {coordinate} {required} has no Maven->NuGet mapping - "
                    f"extend MAVEN_TO_NUGET in {Path(__file__).name}")
            elif expected_nuget.casefold() not in nuget_ids_folded:
                failures.append(
                    f"{project.parent.name}: {artifact}'s .module requires {coordinate} {required}, "
                    f"covered by neither a reference nor the ignore list "
                    f"(expected PackageReference '{expected_nuget}')")

        print(f"  {project.parent.name}: {len(deps)} runtime deps checked")

    # An ignore entry that no .pom mentions any more is a place a real missing dependency can
    # hide - the reason the README asks for a manual re-check on every upgrade.
    for entry in sorted(all_ignores):
        coordinate = ":".join(entry.split(":")[:2])
        if coordinate not in all_pom_deps:
            warnings.append(
                f"ignore entry '{entry}' matches nothing in any current .pom - "
                f"upstream may have cleaned it up; consider removing it")

    for warning in warnings:
        print(f"warning: {warning}", file=sys.stderr)
    for failure in failures:
        print(f"error: {failure}", file=sys.stderr)

    if failures:
        return 1
    print(f"OK: every .module runtime dependency of {len(projects)} projects is declared or ignored.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
