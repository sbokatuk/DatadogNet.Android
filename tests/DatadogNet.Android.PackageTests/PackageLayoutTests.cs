using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DatadogNet.Android.PackageTests;

/// <summary>
/// Asserts the shape of the produced NuGet packages. These run against the packed .nupkg rather
/// than the build output, so they catch packaging regressions the compiler cannot see.
/// </summary>
public class PackageLayoutTests
{
    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Package_carries_a_binding_assembly_for_every_target_framework(string name)
    {
        using var package = Packages.OpenPackage(name);

        foreach (var tfm in Packages.TargetFrameworksFor(name))
        {
            var expected = $"lib/{tfm}/{Packages.AssemblyName(name)}.dll";
            Assert.True(
                package.GetEntry(expected) is not null,
                $"{Packages.PackageId(name)} is missing '{expected}'.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Package_ships_the_native_aar_for_every_target_framework(string name)
    {
        var spec = Packages.Spec(name);

        using var package = Packages.OpenPackage(name);

        foreach (var tfm in Packages.TargetFrameworksFor(name))
        {
            var expected = $"lib/{tfm}/{spec.Artifact}-";
            var aar = package.Entries.SingleOrDefault(entry =>
                entry.FullName.StartsWith(expected, StringComparison.Ordinal) &&
                entry.FullName.EndsWith(".aar", StringComparison.Ordinal));

            // This is the check that catches the silent-empty-package failure. @(AndroidMavenLibrary)
            // does not exist in the .NET Android SDK 34, so on net8 the item is ignored without a
            // word and the package is produced with a 5 KB assembly and no .aar at all.
            Assert.True(
                aar is not null,
                $"{Packages.PackageId(name)} ships no {spec.Artifact} .aar for {tfm}. " +
                "Was the artifact resolved on this target framework?");

            // The smallest real module here is dd-sdk-android-session-replay-material at ~21 KB;
            // an .aar with no classes.jar is a couple of kilobytes.
            Assert.True(
                aar!.Length > 10_000,
                $"'{aar.FullName}' is only {aar.Length} bytes, which is too small to be the real module.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Binding_assembly_contains_types_unless_the_package_only_ships_its_aar(string name)
    {
        using var package = Packages.OpenPackage(name);

        foreach (var tfm in Packages.TargetFrameworksFor(name))
        {
            using var assembly = Packages.ReadEntry(package, $"lib/{tfm}/{Packages.AssemblyName(name)}.dll");
            using var reader = new PEReader(assembly);
            var metadata = reader.GetMetadataReader();

            // Counted by namespace rather than by raw type count: .NET Android always emits its own
            // Resource designer class, so "no types at all" is never true even for a package that
            // binds nothing.
            var datadogTypes = metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Count(type =>
                    type.Attributes.HasFlag(TypeAttributes.Public) &&
                    metadata.GetString(type.Namespace).StartsWith("Com.Datadog", StringComparison.Ordinal));

            if (Packages.ShipOnly.Contains(name))
            {
                // A ship-only package binds nothing on purpose; asserting it stays that way is what
                // stops someone "fixing" it and reintroducing a surface that does not compile.
                Assert.True(
                    datadogTypes == 0,
                    $"{Packages.PackageId(name)} is meant to ship its .aar without binding it, " +
                    $"but its {tfm} assembly declares {datadogTypes} public Com.Datadog types.");
                continue;
            }

            Assert.True(
                datadogTypes > 0,
                $"{Packages.PackageId(name)}'s {tfm} assembly declares no public Com.Datadog types. " +
                "The binding generator produced nothing - most likely the .aar never reached it.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Package_declares_the_expected_sibling_dependencies_for_every_target_framework(string name)
    {
        var spec = Packages.Spec(name);
        var expected = spec.DependsOn.Select(Packages.PackageId).OrderBy(id => id, StringComparer.Ordinal).ToList();

        using var package = Packages.OpenPackage(name);
        var nuspec = Packages.ReadNuspec(package, name);

        var groups = nuspec.Descendants()
            .Where(element => element.Name.LocalName == "group")
            .ToList();

        Assert.Equal(
            Packages.TargetFrameworksFor(name).OrderBy(tfm => tfm, StringComparer.Ordinal),
            groups.Select(group => group.Attribute("targetFramework")?.Value ?? string.Empty)
                  .OrderBy(tfm => tfm, StringComparer.Ordinal));

        // Asserted per group, not just once: the net10 group is grafted in by merge-packages.py
        // from a separately built package, and an empty or stale group there would leave net10
        // consumers restoring a package whose siblings never come with it.
        foreach (var group in groups)
        {
            var declared = group.Elements()
                .Where(element => element.Name.LocalName == "dependency")
                .Select(element => element.Attribute("id")?.Value ?? string.Empty)
                .ToList();

            var siblings = declared
                .Where(id => id.StartsWith("DatadogNet.", StringComparison.Ordinal))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(expected, siblings);

            foreach (var universal in Packages.UniversalDependencies)
            {
                Assert.True(
                    declared.Contains(universal),
                    $"{Packages.PackageId(name)} does not depend on {universal} for " +
                    $"{group.Attribute("targetFramework")?.Value}. Every module needs the Kotlin stdlib.");
            }
        }
    }

    [Fact]
    public void Each_unbound_java_library_is_embedded_in_exactly_one_package()
    {
        // Kronos, JCTools and RE2/J have no .NET binding, so they are embedded as plain Java. If
        // two packages embed the same classes, every consuming app fails to dex with a duplicate
        // class error - a failure that appears in the app's build, never in this repository's.
        foreach (var (owner, classPrefix) in Packages.EmbeddedJavaLibraries)
        {
            var carriers = new List<string>();

            foreach (var spec in Packages.All)
            {
                using var package = Packages.OpenPackage(spec.Name);
                var classes = Packages.JavaClassPaths(package, "net9.0-android35.0");

                if (classes.Any(path => path.StartsWith(classPrefix, StringComparison.Ordinal)))
                {
                    carriers.Add(spec.Name);
                }
            }

            Assert.True(
                carriers.Count == 1,
                $"'{classPrefix}' classes are shipped by [{string.Join(", ", carriers)}]; " +
                $"exactly one package must carry them, and it should be {owner}.");

            Assert.Equal(owner, carriers[0]);
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Package_declares_the_expected_nuspec_metadata(string name)
    {
        using var package = Packages.OpenPackage(name);
        var nuspec = Packages.ReadNuspec(package, name);

        string Value(string element) => nuspec.Descendants()
            .FirstOrDefault(node => node.Name.LocalName == element)?.Value.Trim() ?? string.Empty;

        Assert.Equal(Packages.PackageId(name), Value("id"));
        Assert.NotEmpty(Value("version"));
        Assert.Equal("MIT AND Apache-2.0", Value("license"));
        Assert.Equal("icon.png", Value("icon"));
        Assert.Equal("README.md", Value("readme"));

        // The description names the module the package wraps, which is what tells a reader on
        // nuget.org which of the thirteen they want.
        Assert.Contains(Packages.Spec(name).Artifact, Value("description"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Package_ships_the_icon_readme_and_every_licence_text(string name)
    {
        using var package = Packages.OpenPackage(name);

        Assert.True(package.GetEntry("icon.png") is not null, "icon.png is referenced but not packed.");
        Assert.True(package.GetEntry("README.md") is not null, "README.md is referenced but not packed.");

        using var bindings = new StreamReader(Packages.ReadEntry(package, "licenses/LICENSE"));
        Assert.Contains("MIT License", bindings.ReadToEnd(), StringComparison.OrdinalIgnoreCase);

        using var native = new StreamReader(Packages.ReadEntry(package, "licenses/Apache-2.0.txt"));
        Assert.Contains("Apache License", native.ReadToEnd(), StringComparison.Ordinal);

        // Apache-2.0 section 4(d) requires propagating the NOTICE of a work that carries one, and
        // dd-sdk-android does.
        using var notice = new StreamReader(Packages.ReadEntry(package, "licenses/NOTICE"));
        Assert.Contains("Datadog", notice.ReadToEnd(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packages.Names), MemberType = typeof(Packages))]
    public void Symbol_package_is_produced(string name)
    {
        using var symbols = Packages.OpenPackage(name, ".snupkg");

        foreach (var tfm in Packages.TargetFrameworksFor(name))
        {
            var expected = $"lib/{tfm}/{Packages.AssemblyName(name)}.pdb";
            Assert.True(
                symbols.GetEntry(expected) is not null,
                $"Symbol package for {Packages.PackageId(name)} is missing '{expected}'.");
        }
    }

    [Fact]
    public void Every_package_is_packed_with_the_same_version()
    {
        var versions = Packages.All
            .Select(spec =>
            {
                using var package = Packages.OpenPackage(spec.Name);
                var nuspec = Packages.ReadNuspec(package, spec.Name);
                return nuspec.Descendants().First(node => node.Name.LocalName == "version").Value.Trim();
            })
            .Distinct()
            .ToList();

        // The packages depend on each other at an exact version, so a set built at two different
        // versions does not restore at all.
        Assert.Single(versions);
    }

    [Theory]
    [InlineData("WebView", "datadog-webview.pro")]
    [InlineData("Ndk", "datadog-ndk.pro")]
    public void Reflection_entry_points_ship_consumer_keep_rules(string name, string rules)
    {
        using var package = Packages.OpenPackage(name);

        // WebViewTracking and NdkCrashReports are reached from .NET through JNI alone - no Java
        // code references them - so a consumer's R8 shrink removes them and Enable throws
        // ClassNotFoundException in Release builds only. The keep-rules ride buildTransitive/,
        // where NuGet imports the .targets into every consuming project; this asserts they stay
        // in the package. The DeviceTests project documents the same hazard from the other side.
        Assert.NotNull(package.GetEntry($"buildTransitive/{Packages.PackageId(name)}.targets"));
        Assert.NotNull(package.GetEntry($"buildTransitive/{rules}"));
    }

    [Fact]
    public void Every_expected_package_was_built_and_nothing_else()
    {
        var found = Directory.GetFiles(Packages.ArtifactsDirectory, "*.nupkg")
            .Select(path => Path.GetFileName(path)!)
            .Select(file => file[..file.LastIndexOf(".Android.", StringComparison.Ordinal)] + ".Android")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var expected = Packages.All
            .Select(spec => Packages.PackageId(spec.Name))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Catches both halves of a mistake in packages.tsv: a package silently dropped from the
        // list, and a stale package left in artifacts/ from an earlier version.
        Assert.Equal(expected, found);
    }
}
