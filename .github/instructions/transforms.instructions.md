---
applyTo: "src/**/Transforms/*.xml"
---

# Metadata transforms

These files shape the generated C# API, so every rule is a public-surface decision.

- Explain each rule in a comment: what the generator got wrong, what the rule removes or renames,
  and what the API loses because of it. Every committed rule already does.
- Prefer the narrowest transform that fixes the build. Removing a whole type usually takes members
  of other types with it.
- A rule that stops matching becomes a `BG8A00` **warning**, not an error — so a rule upstream has
  fixed lingers silently. Re-check every file on a native version bump.
- Transforms are picked up by convention (`Transforms/Metadata.xml`, `EnumFields.xml`,
  `EnumMethods.xml`); no `.csproj` edit is needed to add one.
- Verify changes with `./build/BuildNugets.sh`, then
  `dotnet test tests/DatadogNet.Android.PackageTests`, then the emulator smoke test
  (`./.github/scripts/run-emulator-tests.sh <version> <tfm>`) — a renamed or removed member compiles
  fine here and fails in a consumer.
