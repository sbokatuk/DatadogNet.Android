---
applyTo: "src/Datadog.Binding.props,src/**/*.csproj"
---

# Binding project MSBuild

The comments in these files record failure modes that already cost a debugging session each.
Preserve them when editing, and extend them when you add a trap of your own.

- Each `.csproj` is only its identity (`DatadogArtifact`, `DatadogPackageName`, `Description`,
  `PackageTags`), its `Import` of `../Datadog.Binding.props`, and its dependencies. Anything shared
  belongs in `Datadog.Binding.props`; anything versioned belongs in `Directory.Build.props`.
- `Datadog.Binding.props` is imported from the **middle** of each project, after the project has set
  the properties it reads. Projects declare `@(DatadogMavenArtifact)` rows both above and below the
  import, so project them to `@(AndroidMavenLibrary)`/`@(AndroidLibrary)` **in a target**, never in
  an `ItemGroup` — an `ItemGroup` there sees only the rows declared above the import.
- Compare `$(TargetFramework)` literally. During the outer cross-targeting build it is empty, and a
  property function on it aborts evaluation of the whole file, surfacing as
  `The TargetFramework value '' was not recognized`.
- Make paths absolute inside targets. `$(IntermediateOutputPath)` is not final at evaluation time,
  and a stale captured path surfaces much later as `ExtractJarsFromAar` calling the `.aar` corrupt.
- Scheduling is deliberate: `BeforeTargets="_CategorizeAndroidLibraries"` gets a target into the
  build on both resolution paths, and `DependsOnTargets` (not `AfterTargets`) is what orders it
  after the producer. `_MavenRestore` does not exist in the net8 band — depend on it conditionally
  or the build fails with MSB4057.
- New `@(DatadogMavenArtifact)` rows need a SHA-256 in `build/maven-checksums.txt`
  (`./build/UpdateMavenChecksums.sh`), or the build fails naming the missing pin.
- Sibling references are `ProjectReference` with `JavaArtifact` metadata, so Java dependency
  verification can see what the referenced project binds. Prefer that over an
  `AndroidIgnoredJavaDependency`, which hides a real missing dependency just as well as a fake one.
- After changing any of this, `./build/BuildNugets.sh` then
  `dotnet test tests/DatadogNet.Android.PackageTests` — an assembly that silently binds nothing is
  caught only by the size assertion there.
