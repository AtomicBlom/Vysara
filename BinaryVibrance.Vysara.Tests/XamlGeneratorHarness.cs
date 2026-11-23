using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using BinaryVibrance.Vysara.SourceGen;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Drives <see cref="XamlSourceGenerator"/> in-process via <see cref="CSharpGeneratorDriver"/> over a synthetic
/// compilation. The committed Sample/Binding views cover the happy path end to end (build-time generation); this
/// harness covers what that path cannot: diagnostics for <em>invalid</em> XAML (which would break this project's
/// own build) and incremental-caching behaviour (which needs two driver runs and step tracking).
/// </summary>
internal static class XamlGeneratorHarness
{
	private static readonly MetadataReference[] References = BuildReferences();

	private static MetadataReference[] BuildReferences()
	{
		// The running app's trusted-platform assemblies give the BCL refs; add the Vysara runtime assembly whose
		// types the generator resolves XAML elements / properties / events / x:Class against.
		var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
		var refs = tpa.Split(System.IO.Path.PathSeparator)
			.Where(p => p.Length > 0)
			.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
			.ToList();
		refs.Add(MetadataReference.CreateFromFile(typeof(BinaryVibrance.Vysara.Controls.Button).Assembly.Location));
		return [.. refs];
	}

	private sealed class XamlText(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;
		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
	}

	/// <summary>The name of the assembly the views are compiled into, which a library's InternalsVisibleTo names.</summary>
	public const string AssemblyName = "KaldraXamlGenTest";

	/// <summary>Compile <paramref name="source"/> into an assembly named <paramref name="name"/> against the Vysara runtime,
	/// for a view's compilation to reference as metadata, as it would a NuGet package or another project.</summary>
	public static MetadataReference Library(string name, string source)
	{
		var library = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using var image = new System.IO.MemoryStream();
		var emitted = library.Emit(image);
		if (!emitted.Success)
			throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		return MetadataReference.CreateFromImage(image.ToArray());
	}

	/// <summary>Build a driver (with step tracking) + a compilation, ready to run — exposed for incremental-rerun tests.
	/// <paramref name="libraries"/> are further assemblies the compilation references, as <see cref="Library"/> makes.</summary>
	public static (GeneratorDriver Driver, Compilation Compilation) Create(string xaml, string? codeBehind = null, string xamlPath = "Test.xaml",
		MetadataReference[]? libraries = null)
	{
		var trees = codeBehind is null ? [] : new[] { CSharpSyntaxTree.ParseText(codeBehind) };
		var compilation = CSharpCompilation.Create(AssemblyName, trees, [.. References, .. libraries ?? []],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var driver = CSharpGeneratorDriver.Create(
			generators: [new XamlSourceGenerator().AsSourceGenerator()],
			additionalTexts: [new XamlText(xamlPath, xaml)],
			parseOptions: CSharpParseOptions.Default,
			optionsProvider: null,
			driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

		return (driver, compilation);
	}

	/// <summary>Swap the driver's .xaml for an edited version, as an IDE does on a XAML Hot Reload edit, ready to run
	/// again against the same compilation.</summary>
	public static GeneratorDriver Edit(GeneratorDriver driver, string xaml, string xamlPath = "Test.xaml") =>
		driver.ReplaceAdditionalTexts([new XamlText(xamlPath, xaml)]);

	/// <summary>Run the generator once and return the run result (generated sources + diagnostics).</summary>
	public static GeneratorDriverRunResult Run(string xaml, string? codeBehind = null, string xamlPath = "Test.xaml", MetadataReference[]? libraries = null)
	{
		var (driver, compilation) = Create(xaml, codeBehind, xamlPath, libraries);
		return driver.RunGenerators(compilation).GetRunResult();
	}

	/// <summary>
	/// Run the generator, fold its generated sources back into the compilation, and return the C# <em>compile</em>
	/// errors — proving the emitted code is not just the right text but actually compiles against the Vysara runtime.
	/// </summary>
	public static Diagnostic[] CompileErrors(string xaml, string codeBehind, string xamlPath = "Test.xaml", MetadataReference[]? libraries = null)
	{
		var (driver, compilation) = Create(xaml, codeBehind, xamlPath, libraries);
		var result = driver.RunGenerators(compilation).GetRunResult();
		var withGenerated = compilation.AddSyntaxTrees(
			result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SyntaxTree));
		return [.. withGenerated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];
	}

	/// <summary>Run the generator over several .xaml views in one compilation — e.g. to prove hint-name uniqueness.</summary>
	public static GeneratorDriverRunResult RunMany((string Path, string Xaml)[] files, string? codeBehind = null)
	{
		var trees = codeBehind is null ? [] : new[] { CSharpSyntaxTree.ParseText(codeBehind) };
		var compilation = CSharpCompilation.Create(AssemblyName, trees, References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var texts = files.Select(f => (AdditionalText)new XamlText(f.Path, f.Xaml)).ToArray();

		var driver = CSharpGeneratorDriver.Create(
			generators: [new XamlSourceGenerator().AsSourceGenerator()],
			additionalTexts: texts,
			parseOptions: CSharpParseOptions.Default,
			optionsProvider: null,
			driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

		return driver.RunGenerators(compilation).GetRunResult();
	}

	/// <summary>The generator's reported diagnostic ids (e.g. <c>VYSARA003</c>), in report order.</summary>
	public static string[] DiagnosticIds(this GeneratorDriverRunResult result) =>
		[.. result.Diagnostics.Select(d => d.Id)];

	/// <summary>The concatenated text of every generated source — convenient for substring assertions.</summary>
	public static string AllGeneratedText(this GeneratorDriverRunResult result) =>
		string.Join("\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

	/// <summary>The hint names of every generated source (the AddSource keys; must be unique across the compilation).</summary>
	public static string[] HintNames(this GeneratorDriverRunResult result) =>
		[.. result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName)];
}
