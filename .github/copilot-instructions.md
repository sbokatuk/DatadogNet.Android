# DatadogNet.Android

.NET for Android / .NET MAUI bindings over the native Datadog Android SDK
(`DataDog/dd-sdk-android`), currently **dd-sdk-android 3.12.1**, packaged as **3.12.1.4**.

## Overview

- Thirteen NuGet packages, `DatadogNet.<Name>.Android`: Internal, Core, Logs, TraceApi,
  TraceInternal, Trace, RUM, SessionReplay, SessionReplayMaterial, SessionReplayCompose, Ndk,
  WebView, OkHttp. One binding project each under `src/`.
- Versions are `<dd-sdk-android version>.<binding revision>`. Every upstream module releases in
  lock-step at one version, so `DatadogNativeVersion` + `DatadogBindingRevision` in
  `Directory.Build.props` cover all thirteen.
- `build/packages.tsv` is the roster: data rows (`name` / `maven-artifact-id` / `dd-deps`, in
  dependency order) plus a commented NOT-BOUND section giving the reason each remaining
  `dd-sdk-android*` artifact is excluded. The build script, both publish workflows,
  `run-emulator-tests.sh`, the package tests and `upstream-watch.yml` all read it.
- Siblings: [`DatadogNet.iOS`](https://github.com/sbokatuk/DatadogNet.iOS),
  [`DatadogNet.Mac`](https://github.com/sbokatuk/DatadogNet.Mac), and the
  [`DatadogNet`](https://github.com/sbokatuk/DatadogNet) umbrella, which pins these packages — a
  release here needs a follow-up bump there.

## Build and verify

Linux or macOS. Needs the .NET 9 **and** .NET 10 SDKs with the `android` workload installed per
band, a JDK (class-parse and R8 rule generation need it), and Android platforms 34, 35 and 36.
`global.json` pins the .NET 9 SDK, so anything net10 runs from a scratch `global.json`.

```bash
./build/CheckReadmeVersions.sh                    # CI's first step
./build/BuildNugets.sh                            # two passes (net9 + net10 bands), merged -> artifacts/
dotnet test tests/DatadogNet.Android.PackageTests
./.github/scripts/run-emulator-tests.sh 3.12.1.4 net9.0-android35.0   # emulator must be booted
```

- Nothing native is committed and there is no fetch script: artifacts resolve from Maven at build
  time and are hash-checked on every build.
- Sample (MAUI 10 only, consumes the packed nupkgs through the `artifacts/` feed in `NuGet.config`):
  `cd /tmp && dotnet new globaljson --sdk-version 10.0.301 --force`, then
  `dotnet build <repo>/samples/DatadogNet.Android.Example/DatadogNet.Android.Example.csproj -p:DatadogPackageVersion=<version>`.
  MAUI 9 cannot build against this AndroidX generation (a .NET Android SDK NullReferenceException,
  an SDK defect); do not "fix" the sample by retargeting it.
- `DatadogNet.sln` holds `src/` and `tests/` only — the sample is deliberately outside it so
  `dotnet build DatadogNet.sln` needs no MAUI workload.

## Layout

| Path | What |
| --- | --- |
| `src/DatadogNet.<Name>.Android/` | One binding project: identity, dependencies, `Transforms/Metadata.xml`, `Additions/`, generated `buildTransitive/`. |
| `src/Datadog.Binding.props` | Everything shared: TFM bands, namespaces, the `@(DatadogMavenArtifact)` machinery, checksum verification. |
| `build/packages.tsv` | The package roster (bound + deliberately not bound). |
| `build/BuildNugets.sh`, `merge-packages.py` | Pack twice, graft the net10 assets into the net9 packages. |
| `build/BumpNativeVersion.sh` | The whole scripted upgrade chain; stops at the first failure. |
| `build/UpdateMavenChecksums.sh`, `maven-checksums.txt`, `datadog-release-signing-key.asc` | SHA-256 pins, regenerated from publisher `.sha256` sidecars and PGP-verified against Datadog's pinned key fingerprint. |
| `build/generate-r8-rules.sh` | Regenerates every `src/*/buildTransitive/*.pro`. |
| `build/verify-transitive-deps.py` | Declared deps vs upstream Gradle `.module` metadata. |
| `build/check-upstream.sh`, `upstream.tsv` | Version-drift watcher; runnable locally with `DRIFT_DIR=/tmp/drift`. |
| `tests/DatadogNet.Android.PackageTests/` | xunit over `artifacts/`: layout, `.aar` presence, binding-assembly size, per-package TFMs, sibling deps, keep-rules. |
| `tests/DatadogNet.Android.DeviceTests/` | On-emulator smoke app consuming the packed packages. |
| `samples/DatadogNet.Android.Example/` | MAUI app doing the same, the way an app would. |
| `docs/release-notes/<version>.md` | One curated note per version; packed into the nupkg and used as the GitHub release body. |

## Conventions

- Namespaces are the projected Java packages (`Com.Datadog.Android.*`). `RootNamespace` is
  `DatadogNet.Bindings.<Name>` so the generated Resource designer never creates a `DatadogNet.Logs`
  / `.Trace` / `.SessionReplay` / `.Core` namespace — that shadows the bound types in consumers.
- TFMs: net9 band `net8.0-android34.0;net9.0-android35.0`, net10 band `net10.0-android36.0`;
  `SupportedOSPlatformVersion` 23; `AndroidClassParser` class-parse, `AndroidCodegenTarget`
  XAJavaInterop1. Only `DatadogNet.SessionReplayCompose.Android` sets `DatadogSkipNet8=true`.
- Third-party pins live in `Directory.Build.props` and move as one coherent AndroidX/Kotlin
  generation — deliberately not latest, held where net8 assets still exist.
- Licence expression is `MIT AND Apache-2.0` for every package.
- Keep the exhaustive comments in `Directory.Build.props`, `src/Datadog.Binding.props`, the
  `.csproj` files and the transforms: each records a failure that has already happened once.
- British spelling in prose, to match the README.

## CI and release flow

- `pr.yml` → `build.yml` (`pack` → `sample` + `e2e`, all ubuntu-latest; no macOS anywhere) and
  publishes `<version>-beta.<pr>.<run>` to nuget.org via OIDC. Fork PRs build but skip publish.
- Merging a PR that **adds** `docs/release-notes/<4-part>.md` makes `auto-release.yml` tag the
  merge commit and dispatch `release.yml`.
- `release.yml`: `guard` (the tag must be an ancestor of the default branch) and a version check
  (the tag's native line must match `Directory.Build.props`) → `build.yml` with `verify: false` →
  push all thirteen packages together → `gh release create`.
- `upstream-drift.yml` runs daily against `build/upstream.tsv`; `upstream-watch.yml` weekly against
  the `packages.tsv` roster. Both file issues; act on them, don't silence them.

## Testing

- Run `dotnet test tests/DatadogNet.Android.PackageTests` after every pack — it is what catches a
  package losing a target framework, shipping an empty 5 KB binding assembly, losing its `.aar`,
  or losing its keep-rules.
- Run the emulator smoke tests for binding, manifest, dependency or R8 changes. CI runs net8, net10
  and one shrunk (`r8`) net10 leg.
- net8 has no Java dependency verification (its SDK predates `@(AndroidMavenLibrary)`), so a net9 or
  net10 failure is the authoritative one; a net8-only failure points at the download fallback.

## Hard rules

- **Never commit `.aar`/`.jar` files.** Artifacts resolve from Maven at build time and must
  hash-match `build/maven-checksums.txt`. Never set `DatadogVerifyMavenChecksums=false` in
  committed code.
- **A hash that changes for an unchanged version is an incident**, not a pin to regenerate.
  Investigate before touching `maven-checksums.txt`; only artifacts whose version changed may have
  a changed hash.
- **Never bump one AndroidX/Kotlin pin in isolation** — the set moves together or not at all.
  Respect the net8-asset constraint and the OkHttp **4.12.0** ceiling (upstream compiles against it
  and exposes `okhttp3` types publicly, so no 5.x).
- **R8 rules are generated.** Edit `build/generate-r8-rules.sh`, never `src/*/buildTransitive/*.pro`;
  CI regenerates and fails on drift.
- **Adding or removing a package** means a `build/packages.tsv` row plus a project under `src/` —
  and keeping the NOT-BOUND roster current, with full artifact ids, because `upstream-watch.yml`
  greps them.
- **Version bumps go through `./build/BumpNativeVersion.sh <version>`**, and README pins must follow
  (`./build/CheckReadmeVersions.sh` is CI's first step).
- **Release only via the workflows.** Never hand-push packages to nuget.org, never bypass the
  `guard` job, and never edit an existing release note to trigger a release (only added files tag).

## References

- [DataDog/dd-sdk-android](https://github.com/DataDog/dd-sdk-android) ·
  [Datadog Android RUM docs](https://docs.datadoghq.com/real_user_monitoring/mobile_and_tv_monitoring/android/)
- [Maven Central `com.datadoghq`](https://repo1.maven.org/maven2/com/datadoghq/) — artifact roster,
  `.pom`, `.module`, `.sha256` and `.asc` sidecars
- [.NET for Android bindings](https://learn.microsoft.com/en-us/dotnet/android/binding-libs/binding-java-libs/) ·
  [`@(AndroidMavenLibrary)`](https://learn.microsoft.com/en-us/dotnet/android/binding-libs/advanced-concepts/android-maven-library) ·
  [metadata transforms](https://learn.microsoft.com/en-us/dotnet/android/binding-libs/customizing-bindings/java-bindings-metadata)
- Siblings: [`DatadogNet.iOS`](https://github.com/sbokatuk/DatadogNet.iOS) ·
  [`DatadogNet.Mac`](https://github.com/sbokatuk/DatadogNet.Mac) ·
  [`DatadogNet`](https://github.com/sbokatuk/DatadogNet)

Trust these instructions, and search the codebase only when something here is incomplete or turns
out to be wrong.
