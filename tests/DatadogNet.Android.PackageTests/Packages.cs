using System.IO.Compression;
using System.Xml.Linq;

namespace DatadogNet.Android.PackageTests;

/// <summary>What one package is expected to be.</summary>
/// <param name="Name">The middle segment of the id: <c>DatadogNet.<see cref="Name"/>.Android</c>.</param>
/// <param name="Artifact">The Maven artifactId the package wraps.</param>
/// <param name="DependsOn">The <see cref="Name"/>s of the sibling packages it must depend on.</param>
public sealed record PackageSpec(string Name, string Artifact, string[] DependsOn);

/// <summary>
/// Locates the packed .nupkg files and describes what each one is supposed to contain.
/// </summary>
public static class Packages
{
    /// <summary>
    /// Every package this repository builds, read from build/packages.tsv.
    /// </summary>
    /// <remarks>
    /// Read from the manifest rather than restated here, because the manifest is what the build
    /// script packs and what the release workflow lists. A copy in the tests would let the two
    /// drift and still pass.
    /// <para>
    /// What is <em>not</em> read from the manifest is the expectation for third-party dependencies
    /// and embedded Java libraries below - those are stated independently on purpose, so the test
    /// disagrees with the projects rather than echoing them.
    /// </para>
    /// </remarks>
    public static readonly PackageSpec[] All = LoadManifest();

    /// <summary>Target frameworks a package carries a binding assembly for, unless noted below.</summary>
    public static readonly string[] ExpectedTargetFrameworks =
    [
        "net8.0-android34.0", "net9.0-android35.0", "net10.0-android36.0",
    ];

    /// <summary>
    /// Packages that deliberately do not ship net8, and why.
    /// </summary>
    /// <remarks>
    /// Stated here rather than derived from what was built, so that a package losing net8 by
    /// accident - a mis-set property, a dependency that quietly drops it - fails instead of being
    /// accepted as the new shape.
    /// </remarks>
    public static readonly string[] SkipsNet8 = ["SessionReplayCompose"];

    /// <summary>The target frameworks a particular package must carry.</summary>
    public static string[] TargetFrameworksFor(string name) =>
        SkipsNet8.Contains(name)
            ? ExpectedTargetFrameworks.Where(tfm => !tfm.StartsWith("net8.", StringComparison.Ordinal)).ToArray()
            : ExpectedTargetFrameworks;

    /// <summary>
    /// Packages that ship their .aar without binding it, so their assembly is legitimately tiny.
    /// </summary>
    public static readonly string[] ShipOnly = ["TraceInternal"];

    /// <summary>
    /// Third-party Java libraries with no .NET binding, and the single package that embeds each.
    /// </summary>
    /// <remarks>
    /// Ownership is the point. The same classes contributed by two packages are dexed twice and
    /// fail the consuming app's build with a duplicate class error, which is invisible here unless
    /// asserted.
    /// </remarks>
    public static readonly (string Owner, string ClassPrefix)[] EmbeddedJavaLibraries =
    [
        ("Core", "com/lyft/kronos"),
        ("TraceApi", "org/jctools"),
        ("TraceApi", "com/google/re2j"),
    ];

    /// <summary>NuGet packages every binding must depend on, whatever else it declares.</summary>
    public static readonly string[] UniversalDependencies = ["Xamarin.Kotlin.StdLib"];

    /// <summary>xunit member data: one row per package.</summary>
    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var package in All)
            {
                data.Add(package.Name);
            }

            return data;
        }
    }

    public static PackageSpec Spec(string name) =>
        All.Single(package => package.Name == name);

    public static string PackageId(string name) => $"DatadogNet.{name}.Android";

    public static string AssemblyName(string name) => PackageId(name);

    /// <summary>
    /// The directory packages are read from. Overridable so the tests can run against a directory
    /// other than the repository's own artifacts/ - a CI job that downloads them, for instance.
    /// </summary>
    public static string ArtifactsDirectory =>
        Environment.GetEnvironmentVariable("DATADOG_ARTIFACTS_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(RepositoryRoot, "artifacts");

    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    private static PackageSpec[] LoadManifest()
    {
        var path = Path.Combine(RepositoryRoot, "build", "packages.tsv");
        var specs = new List<PackageSpec>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 3)
            {
                continue;
            }

            var dependsOn = columns[2].Trim() is "-" or ""
                ? []
                : columns[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            specs.Add(new PackageSpec(columns[0].Trim(), columns[1].Trim(), dependsOn));
        }

        if (specs.Count == 0)
        {
            throw new InvalidOperationException($"No packages were read from {path}.");
        }

        return [.. specs];
    }

    public static ZipArchive OpenPackage(string name, string extension = ".nupkg")
    {
        var id = PackageId(name);
        var matches = Directory.GetFiles(ArtifactsDirectory, $"{id}.*{extension}");

        // Matching on the id prefix would also match a longer id that starts with it -
        // "Trace" against "TraceApi" and "TraceInternal" - so the next character after the id must
        // look like the start of a version.
        var package = matches.SingleOrDefault(path =>
            Path.GetFileName(path).StartsWith($"{id}.", StringComparison.Ordinal) &&
            char.IsDigit(Path.GetFileName(path)[id.Length + 1]));

        if (package is null)
        {
            throw new FileNotFoundException(
                $"No {id}{extension} in {ArtifactsDirectory}. Run ./build/BuildNugets.sh first.");
        }

        return ZipFile.OpenRead(package);
    }

    /// <summary>Reads an entry into a seekable stream, so it can be opened as an archive.</summary>
    public static MemoryStream ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidOperationException($"Archive has no entry '{path}'.");

        var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;
        return buffer;
    }

    public static XDocument ReadNuspec(ZipArchive package, string name)
    {
        using var stream = ReadEntry(package, $"{PackageId(name)}.nuspec");
        return XDocument.Load(stream);
    }

    /// <summary>Every file under <c>lib/&lt;tfm&gt;/</c> in a package.</summary>
    public static List<string> LibEntries(ZipArchive package, string tfm) =>
        package.Entries
            .Where(entry => entry.FullName.StartsWith($"lib/{tfm}/", StringComparison.Ordinal))
            .Select(entry => entry.FullName[$"lib/{tfm}/".Length..])
            .Where(entry => entry.Length > 0)
            .ToList();

    /// <summary>
    /// The Java class-file paths a package contributes, across its own generated .aar and every
    /// .aar it ships alongside it.
    /// </summary>
    public static HashSet<string> JavaClassPaths(ZipArchive package, string tfm)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in LibEntries(package, tfm).Where(e => e.EndsWith(".aar", StringComparison.Ordinal)))
        {
            using var aarStream = ReadEntry(package, $"lib/{tfm}/{name}");
            using var aar = new ZipArchive(aarStream, ZipArchiveMode.Read);

            foreach (var inner in aar.Entries.Where(e => e.FullName.EndsWith(".jar", StringComparison.Ordinal)))
            {
                using var jarStream = new MemoryStream();
                using (var open = inner.Open())
                {
                    open.CopyTo(jarStream);
                }

                jarStream.Position = 0;
                using var jar = new ZipArchive(jarStream, ZipArchiveMode.Read);
                foreach (var entry in jar.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)))
                {
                    paths.Add(entry.FullName);
                }
            }
        }

        return paths;
    }
}
