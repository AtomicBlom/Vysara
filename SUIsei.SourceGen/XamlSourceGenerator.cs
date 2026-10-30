using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SUIsei.SourceGen;

/// <summary>
/// The XAML → typed control-tree incremental generator. For each <c>.xaml</c> <see cref="AdditionalText"/> it
/// emits a <c>partial</c> of the <c>x:Class</c> with typed <c>x:Name</c> fields and an
/// <c>InitializeComponent()</c> that builds the tree with generated <c>new</c> (never reflection). Types resolve through
/// their XML namespace, and every value converts for its property's type through one compile-time converter table,
/// both against the <see cref="Compilation"/> semantic model.
/// <para>
/// Compiled data + event binding: <c>{x:Bind path[, Mode=OneTime|OneWay|TwoWay][, FallbackValue=X][, TargetNullValue=Y]}</c>
/// on a property emits a typed <c>&lt;Class&gt;_Bindings</c> helper (Initialize/Update/Subscribe/StopTracking +
/// INPC tracking that switches on <c>PropertyName</c> literals). TwoWay additionally wires the control's
/// <c>{PropertyName}Changed</c> event back to the VM. An event attribute wires <c>el.Event += this.Handler</c>.
/// <c>{StaticResource key}</c> emits a <c>FindResource("key")</c> lookup through the markup ancestors that declared
/// resources; <c>{x:Null}</c> and <c>{x:Static}</c> resolve at compile time.
/// Function paths and converters are outside the supported subset and diagnose.
/// </para>
/// <para>
/// The source table: <c>InitializeComponent()</c> and each template factory end by recording, through
/// <c>SUIsei.Diagnostics.RecordSource</c>, the file, line and column of every element they built and every style setter,
/// with a <c>ControlTemplate</c>'s elements marked as template elements. The calls sit in one block behind
/// <c>SUIsei.Diagnostics.Enabled</c>, so they cost one check where diagnostics are off and the trimmer removes them where
/// the switch is fixed off, as under NativeAOT.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class XamlSourceGenerator : IIncrementalGenerator
{
	private const string TrackName = "XamlGenerate";

	private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
	// The SUIsei UI namespace (the default xmlns of a SUIsei view). Elements live in it, in the extension namespace or in
	// a `using:` CLR namespace; one in any other namespace is foreign and is never resolved as if it were a SUIsei type.
	private static readonly XNamespace Ui = "https://github.com/AtomicBlom/SUIsei/ui";
	// The SUIsei extension namespace (conventionally `xmlns:suisei`): SUIsei-only types and attached properties with no
	// WinUI equivalent, kept out of the default namespace so WinUI types keep exactly WinUI's surface.
	private static readonly XNamespace ExtensionNs = "https://github.com/AtomicBlom/SUIsei/extensions";

	// Markup compatibility: mc:Ignorable names the prefixes (typically d:) whose attributes and elements are skipped.
	private static readonly XNamespace MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

	private static readonly DiagnosticDescriptor ParseError = new("SUISEI001", "Invalid XAML",
		"XAML parse error: {0}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor MissingClass = new("SUISEI002", "Missing x:Class",
		"The root element needs an x:Class attribute for the generator to emit a partial class", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownElement = new("SUISEI003", "Unknown element",
		"'{0}' is not a known SUIsei type (the default XAML namespace maps SUIsei.Controls, SUIsei, SUIsei.Media, SUIsei.Shapes, SUIsei.Documents and SUIsei.Animation)", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownProperty = new("SUISEI004", "Unknown property",
		"'{0}' has no settable property '{1}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnsupportedProperty = new("SUISEI005", "Unsupported property type",
		"Property '{0}' of type '{1}' is outside the XAML subset (string / numeric / bool / enum)", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor NoContentModel = new("SUISEI006", "Element cannot hold children",
		"'{0}' has no Children/Child/Content to host the child '{1}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnsupportedMarkup = new("SUISEI007", "Unsupported markup",
		"Markup '{0}' is outside the XAML subset (supported: {{x:Bind path[, Mode=OneTime|OneWay|TwoWay][, FallbackValue=X][, TargetNullValue=Y]}}, {{StaticResource key}}, event handlers)", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BindPathUnresolved = new("SUISEI008", "Unresolved x:Bind path",
		"x:Bind path '{0}' could not be resolved on '{1}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BindTypeMismatch = new("SUISEI009", "Incompatible x:Bind types",
		"x:Bind source type '{0}' is not assignable to target property '{1}' of type '{2}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor EventHandlerSignature = new("SUISEI010", "Bad event handler",
		"'{0}' has no handler '{1}(object, RoutedEventArgs)' for event '{2}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidName = new("SUISEI011", "Invalid x:Name",
		"x:Name '{0}' is not usable as a field — it must be a unique, non-reserved C# identifier", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidLiteral = new("SUISEI012", "Invalid property value",
		"'{0}' is not a valid value for property '{1}' of type '{2}': expected {3}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor IgnoredContent = new("SUISEI013", "Ignored XAML content",
		"Ignored {0} — it is outside the supported XAML subset (elements, literal/enum attributes, {{x:Bind}}, {{StaticResource}}, and event handlers)", "SUIsei", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor TwoWayNoEvent = new("SUISEI014", "TwoWay binding — no changed event",
		"TwoWay binding on '{0}.{1}': no '{1}Changed' event found; binding will be OneWay", "SUIsei", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor BadStoryboardTarget = new("SUISEI015", "Unresolved storyboard target",
		"Storyboard.TargetName '{0}' does not name an x:Name'd element in this view", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadAnimationProperty = new("SUISEI016", "Unanimatable property",
		"Storyboard.TargetProperty '{0}' is a {1} property, which a {2} cannot animate", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadLiteralValue = new("SUISEI017", "Invalid markup value",
		"'{0}' is not a valid {1}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadSetterProperty = new("SUISEI018", "Unstylable property",
		"Setter Property '{0}' does not resolve to a dependency property '{0}Property' on '{1}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TemplateBindingOutsideTemplate = new("SUISEI019", "TemplateBinding outside a template",
		"{{TemplateBinding}} is only valid inside a ControlTemplate", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadPropertyElement = new("SUISEI020", "Unsupported property element",
		"Property element '{0}' is outside the supported XAML subset", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadPropertyPath = new("SUISEI021", "Unresolved property path",
		"Property path '{0}' does not resolve: {1}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BrushOrTransformShorthand = new("SUISEI023", "Animation shorthand for a brush or a transform",
		"Storyboard.TargetProperty '{0}' {1}; write the WinUI path '{2}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownExtensionMember = new("SUISEI022", "Unknown extension member",
		"'{0}' is not a member of the SUIsei extension namespace (SUIsei.Extensions)", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidPathMarkup = new("SUISEI024", "Invalid path markup",
		"'{0}' is not valid path markup for property '{1}', {2}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TemplateValueOnLocal = new("SUISEI027", "Template value set as the part's own",
		"In a ControlTemplate, {0} is set through its setter as the part's local value, not as the template's: {1}", "SUIsei", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor OutsideSubset = new("SUISEI025", "Outside the XAML subset",
		"{0} is outside the XAML subset{1}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor WrongChildType = new("SUISEI026", "Child of the wrong type",
		"'{0}' cannot hold a {1}: its {2} takes a {3}", "SUIsei", DiagnosticSeverity.Error, true);

	// The x: directives SUIsei reads, and where: x:Name on any element (and on a visual state or group), x:Key on a
	// resource, x:Class on the root and x:DataType on a DataTemplate. Every other directive (x:Uid, x:Load, x:Phase,
	// x:DeferLoadStrategy, x:FieldModifier, …) is reported where it is written, not skipped, since skipping it changes
	// what the view does: an x:Load="False" element would be loaded.
	private static bool IsDirective(Gen gen, XElement el, XAttribute attr, bool isRoot = false)
	{
		if (attr.Name.Namespace != X)
			return false;
		var allowed = attr.Name.LocalName switch
		{
			"Name" or "Key" => true,
			"Class" => isRoot,
			"DataType" => el.Name.LocalName == "DataTemplate",
			_ => false,
		};
		if (!allowed)
		{
			gen.Report(OutsideSubset, attr, $"The directive 'x:{attr.Name.LocalName}' on {el.Name.LocalName}", attr.Name.LocalName switch
			{
				"Uid" => " (SUIsei has no localization resources)",
				"Load" or "DeferLoadStrategy" => " (every element is loaded)",
				"Phase" => " (every element is built at once)",
				"Class" => " (only the root element names the class)",
				"DataType" => " (only a DataTemplate takes one)",
				_ => "",
			});
		}
		return true;
	}

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var inputs = context.AdditionalTextsProvider
			.Where(static a => a.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
			.Select(static (a, ct) => new XamlInput(a.Path, a.GetText(ct)?.ToString() ?? ""));

		// Narrow: instead of combining with the raw Compilation (a new reference on every keystroke),
		// compute a fingerprint of the type surface that matters to the generator — the current
		// assembly's public and internal types (page classes, ViewModels, extension types) plus the SUIsei
		// types, with member signatures. Equal fingerprint → the expensive Generate call is skipped even if
		// the compilation reference changed. Body-only edits, whitespace, and comment changes never affect
		// the fingerprint. It is computed once per compilation, not once per XAML file.
		var surface = context.CompilationProvider
			.Select(static (compilation, ct) => (Compilation: compilation, Fingerprint: ComputeFingerprint(compilation, ct)));
		var results = inputs
			.Combine(surface)
			.Select(static (pair, _) => new CompilationSnapshot(pair.Left, pair.Right.Compilation, pair.Right.Fingerprint))
			.Select(static (snap, ct) => Generate(snap.Input, snap.Compilation, ct))
			.WithTrackingName(TrackName);

		context.RegisterSourceOutput(results, static (spc, result) =>
		{
			foreach (var d in result.Diagnostics.AsArray())
				spc.ReportDiagnostic(d.ToDiagnostic());
			foreach (var s in result.Sources.AsArray())
				spc.AddSource(s.HintName, SourceText.From(s.Text, Encoding.UTF8));
		});
	}

	private readonly record struct XamlInput(string Path, string Text);
	private readonly record struct GeneratedSource(string HintName, string Text);

	// Combines the XAML input with the compilation but compares equal only when both the XAML text
	// AND the type surface (fingerprint) are unchanged — ignores the Compilation reference itself,
	// which is a new object on every keystroke. This breaks the "incremental in name only" pattern that
	// ran Generate on every C# edit even when no relevant type had changed.
	private readonly record struct CompilationSnapshot(XamlInput Input, Compilation Compilation, string Fingerprint)
	{
		public bool Equals(CompilationSnapshot other) => Input == other.Input && Fingerprint == other.Fingerprint;
		public override int GetHashCode() => (Input.GetHashCode() * 397) ^ Fingerprint.GetHashCode();
	}

	private readonly record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, string FilePath, TextSpan Span, LinePositionSpan LineSpan, EquatableArray<string> Args)
	{
		public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location.Create(FilePath, Span, LineSpan), Args.AsArray());
	}

	private readonly record struct GenResult(EquatableArray<GeneratedSource> Sources, EquatableArray<DiagnosticInfo> Diagnostics);

	// One resolved x:Bind binding.
	private sealed class Bind(
		string targetVar,
		string targetProp,
		string[] path,
		ITypeSymbol[] resultTypes,
		ITypeSymbol leafType,
		bool oneWay,
		bool twoWay,
		string? fallbackValue,
		string? targetNullValue)
	{
		public string TargetVar { get; } = targetVar;
		public string TargetProp { get; } = targetProp;
		public string[] Path { get; } = path;
		public ITypeSymbol[] ResultTypes { get; } = resultTypes;
		public ITypeSymbol LeafType { get; } = leafType;
		public bool OneWay { get; } = oneWay || twoWay;  // TwoWay also pushes VM→control
		public bool TwoWay { get; } = twoWay;
		public string? FallbackValue { get; } = fallbackValue;
		public string? TargetNullValue { get; } = targetNullValue;
	}

	private sealed class Gen(Compilation compilation, string path, SourceText text, INamedTypeSymbol? pageType, CancellationToken ct)
	{
		public Compilation Compilation { get; } = compilation;
		public string Path { get; } = path;
		public SourceText Text { get; } = text;
		public INamedTypeSymbol? PageType { get; } = pageType;
		public CancellationToken Ct { get; } = ct;
		public List<string> Fields { get; } = [];
		public StringBuilder Body { get; } = new();
		// Extra members appended to the partial class after InitializeComponent — the static template factories.
		public StringBuilder Methods { get; } = new();
		public List<string> EventWires { get; } = [];
		public List<Bind> Bindings { get; } = [];
		public List<DiagnosticInfo> Diagnostics { get; } = [];
		public HashSet<string> UsedNames { get; } = ["Root"];
		public INamedTypeSymbol? RootType { get; set; }
		// SUIsei.UIElement where the referenced SUIsei has the diagnostics source table to record elements into; null where
		// it has none, and then nothing is recorded.
		public INamedTypeSymbol? RecordableElement { get; } =
			compilation.GetTypeByMetadataName("SUIsei.Diagnostics") is null ? null : compilation.GetTypeByMetadataName("SUIsei.UIElement");
		// SUIsei.FrameworkElement, whose Name an x:Name is assigned to.
		public INamedTypeSymbol? FrameworkElementType { get; } = compilation.GetTypeByMetadataName("SUIsei.FrameworkElement");
		public int Counter;

		public void Report(DiagnosticDescriptor descriptor, XObject node, params string[] args) =>
			Diagnostics.Add(MakeDiagnostic(descriptor, Path, Text, node, args));
	}

	// The emission target + binding context for EmitElement. The page scope builds InitializeComponent (named
	// elements become fields; {x:Bind}s defer into the _Bindings helper; events wire to `this`). A template scope
	// builds a static factory method body instead (named elements become locals; {x:Bind}/{TemplateBinding} resolve
	// inline against the template's data root — a data item for a DataTemplate, the templated control for a
	// ControlTemplate), so the two binding scopes never share fields.
	private sealed class Scope
	{
		public StringBuilder Body { get; init; } = new();
		// The x:Name registry for this scope: enforces uniqueness and resolves storyboard targets. The page scope
		// shares the generator's global set; each template gets its own (a name may repeat across templates).
		public HashSet<string> Names { get; init; } = [];
		// Each x:Name'd element's type and the expression that reaches it (a field, a local, or `this` for an unsealed
		// root): what a storyboard's TargetName resolves to.
		public Dictionary<string, (INamedTypeSymbol Type, string Expr)> NamedElements { get; } = [];
		// The markup ancestors (and the current element) whose <X.Resources> are emitted, outermost first: where a
		// {StaticResource} resolved during construction finds its key. A control template starts with the templated control.
		public List<string> ResourceOwners { get; init; } = [];
		public bool IsTemplate { get; init; }
		public bool IsControlTemplate { get; init; }
		// The {x:Bind}/{TemplateBinding} data-root expression (e.g. "__data" / "__tc") and its type, for a template.
		public string DataRootExpr { get; init; } = "";
		public INamedTypeSymbol? DataRootType { get; init; }
		// The RecordSource calls for the elements and style setters this scope builds, emitted together at its end behind the
		// diagnostics switch, so one check covers them and the trimmer drops the lot where the switch is off.
		public List<string> SourceRecords { get; } = [];

		// Append a statement at InitializeComponent / factory-body indentation (both are two tabs deep).
		public void Emit(string statement) => Body.AppendLine("\t\t" + statement);
	}

	// The property child elements flow into, and whether it is a collection (append) or a single slot (assign).
	private readonly record struct ContentTarget(string Name, bool IsCollection);

	private enum LiteralStatus { Ok, BadValue, UnsupportedType }

	private static GenResult Generate(XamlInput input, Compilation compilation, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(input.Text))
			return default;

		var text = SourceText.From(input.Text);

		XDocument doc;
		try { doc = XDocument.Parse(input.Text, LoadOptions.SetLineInfo); }
		catch (XmlException ex) { return new GenResult(default, new[] { MakeDiagnostic(ParseError, input.Path, text, 1, 1, [ex.Message]) }); }

		var root = doc.Root;
		if (root is null)
			return default;

		var classAttr = root.Attribute(X + "Class");
		if (classAttr is null)
			return new GenResult(default, new[] { MakeDiagnostic(MissingClass, input.Path, text, root, []) });

		var (ns, cls) = SplitClass(classAttr.Value);
		var fullClass = classAttr.Value;
		var pageType = compilation.GetTypeByMetadataName(fullClass);
		var gen = new Gen(compilation, input.Path, text, pageType, ct);
		var pageScope = new Scope { Body = gen.Body, Names = gen.UsedNames };

		var rootVar = EmitElement(gen, pageScope, root, isRoot: true);
		if (rootVar is null)
			return new GenResult(default, gen.Diagnostics.ToArray());
		var rootTypeName = gen.RootType?.ToDisplayString() ?? "global::SUIsei.UIElement";
		var rootIsSealed = gen.RootType?.IsSealed ?? true;

		// OneTime bindings (no Mode or Mode=OneTime) are emitted as direct assignments in InitializeComponent.
		// OneWay/TwoWay bindings need a _Bindings tracking class for INPC subscriptions.
		var oneTimeBindings = gen.Bindings.Where(b => !b.OneWay).ToList();
		var trackingBindings = gen.Bindings.Where(b => b.OneWay).ToList();
		var hasBindings = trackingBindings.Count > 0;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/> by SUIsei.SourceGen");
		sb.AppendLine("#nullable enable");
		if (ns is not null)
			sb.AppendLine($"namespace {ns};");
		sb.AppendLine();
		sb.AppendLine(rootIsSealed ? $"partial class {cls}" : $"partial class {cls} : {rootTypeName}");
		sb.AppendLine("{");
		foreach (var f in gen.Fields)
			sb.AppendLine($"\t{f}");
		if (hasBindings)
			sb.AppendLine($"\tprivate {cls}_Bindings? __bindings;");
		sb.AppendLine();
		sb.AppendLine("\tpublic void InitializeComponent()");
		sb.AppendLine("\t{");
		sb.Append(gen.Body);
		foreach (var b in oneTimeBindings)
			sb.AppendLine($"\t\t{b.TargetVar}.{b.TargetProp} = {OneTimeExpr(b)};");
		foreach (var wire in gen.EventWires)
			sb.AppendLine($"\t\t{wire}");
		if (hasBindings)
		{
			sb.AppendLine($"\t\t__bindings = new {cls}_Bindings(this);");
			sb.AppendLine("\t\t__bindings.Initialize();");
		}
		sb.Append(SourceRecordBlock(pageScope));
		// Register for XAML Hot Reload with no-capture delegates (the registry holds only a weak reference). Inert
		// unless hot reload is active; idempotent across rebuilds.
		if (!rootIsSealed)
			sb.AppendLine($"\t\tglobal::SUIsei.HotReload.Track<global::{fullClass}>(this, static __c => __c.RebuildComponent());");
		else
			sb.AppendLine($"\t\tglobal::SUIsei.HotReload.Track(this, static __c => ((global::{fullClass})__c).RebuildComponent(), static __c => (global::SUIsei.UIElement)((global::{fullClass})__c).{rootVar});");
		sb.AppendLine("\t}");
		sb.AppendLine();
		// Re-runs construction in place for XAML Hot Reload: detach the old binding subscriptions, then rebuild the
		// tree (a fresh Root + bindings). A composition root's new Root takes the old one's place, and the code-behind's
		// OnComponentRebuilt runs last, once the rebuilt tree is where the old one was.
		sb.AppendLine("\tpublic void RebuildComponent()");
		sb.AppendLine("\t{");
		if (hasBindings)
			sb.AppendLine("\t\t__bindings?.StopTracking();");
		if (rootIsSealed)
			sb.AppendLine($"\t\tvar __oldRoot = {rootVar};");
		// Only a Panel root needs Children cleared before re-running InitializeComponent; other roots
		// (ContentControl, Border, ScrollViewer, etc.) reset their content via property assignment.
		var panelType = gen.Compilation.GetTypeByMetadataName("SUIsei.Controls.Panel");
		var rootIsPanel = panelType != null && gen.RootType != null && IsAssignableTo(gen.RootType, panelType);
		if (rootIsPanel)
			sb.AppendLine(rootIsSealed ? $"\t\t{rootVar}.Children.Clear();" : "\t\tthis.Children.Clear();");
		sb.AppendLine("\t\tInitializeComponent();");
		if (rootIsSealed)
			sb.AppendLine($"\t\tglobal::SUIsei.HotReload.ReplaceRoot(__oldRoot, {rootVar});");
		sb.AppendLine("\t\tOnComponentRebuilt();");
		sb.AppendLine("\t}");
		sb.AppendLine();
		// The code-behind's hook: it costs nothing when the code-behind leaves it unimplemented.
		sb.AppendLine("\t/// <summary>Runs at the end of <see cref=\"RebuildComponent\"/>, once the rebuilt tree and its bindings are in");
		sb.AppendLine("\t/// place: re-apply here what the code-behind set on the tree in code, which the rebuild discarded.</summary>");
		sb.AppendLine("\tpartial void OnComponentRebuilt();");
		if (gen.Methods.Length > 0)
		{
			sb.AppendLine();
			sb.Append(gen.Methods);
		}
		sb.AppendLine("}");

		var main = new GeneratedSource(MakeHintName(fullClass, ""), sb.ToString());
		var sources = hasBindings
			? new[] { main, EmitBindings(fullClass, ns, cls, trackingBindings) }
			: new[] { main };
		return new GenResult(sources, gen.Diagnostics.ToArray());
	}

	private static string? EmitElement(Gen gen, Scope scope, XElement el, bool isRoot = false)
	{
		gen.Ct.ThrowIfCancellationRequested();

		// Elements come from the default namespace, the extension namespace or a `using:` CLR namespace; any other XML
		// namespace is foreign, not resolved as if it were a SUIsei type.
		var ns = el.Name.Namespace;
		var type = ResolveType(gen.Compilation, ns, el.Name.LocalName);
		if (isRoot)
			gen.RootType = type;
		if (type is null)
		{
			gen.Report(ns == ExtensionNs ? UnknownExtensionMember : UnknownElement, el, el.Name.LocalName);
			return null;
		}
		if (!isRoot && !IsConstructible(type))
		{
			gen.Report(OutsideSubset, el, $"An element of {type.Name}", type.IsAbstract
				? $" ({type.Name} is abstract; write one of the types derived from it)"
				: $" (markup builds an object through a public parameterless constructor, which {type.Name} doesn't have; make it in code)");
			return null;
		}

		var typeName = type.ToDisplayString();
		var nameAttr = el.Attribute(X + "Name");
		var hasName = TryValidateName(gen, scope, nameAttr);

		string var;
		if (scope.IsTemplate)
		{
			// Inside a template factory every element is a local; a named one keeps its name so a storyboard /
			// VSM in the same template can target it.
			var = hasName ? nameAttr!.Value : NewName(gen, "__t");
			scope.Emit($"var {var} = new {typeName}();");
		}
		else if (isRoot && !type.IsSealed)
		{
			var = "this"; // component inherits the XAML root type; properties assign to 'this', no new() emitted
		}
		else if (isRoot) // sealed root: use composition (Root field or named field)
		{
			var = hasName ? nameAttr!.Value : "Root";
			gen.Fields.Add($"internal {typeName} {var} = null!;");
			scope.Emit($"{var} = new {typeName}();");
		}
		else if (hasName)
		{
			var = nameAttr!.Value;
			gen.Fields.Add($"internal {typeName} {var} = null!;");
			scope.Emit($"{var} = new {typeName}();");
		}
		else if (NeedsField(type, el))
		{
			var = NewName(gen, "__b");
			gen.Fields.Add($"internal {typeName} {var} = null!;");
			scope.Emit($"{var} = new {typeName}();");
		}
		else
		{
			var = NewName(gen, "__e");
			scope.Emit($"var {var} = new {typeName}();");
		}

		if (hasName)
			scope.NamedElements[nameAttr!.Value] = (type, var);
		RecordElementSource(gen, scope, el, type, var);

		// The element carries its x:Name at run time, as in WinUI, which is what a diagnostics tool addresses it by.
		if (hasName && gen.FrameworkElementType is { } frameworkElement && IsAssignableTo(type, frameworkElement))
			scope.Emit($"{var}.Name = {Quote(nameAttr!.Value)};");

		foreach (var attr in el.Attributes())
		{
			var attrNs = attr.Name.Namespace;
			if (attr.IsNamespaceDeclaration || IsIgnorable(el, attrNs) || IsDirective(gen, el, attr, isRoot))
				continue;
			if (attrNs == ExtensionNs || attr.Name.LocalName.Contains('.'))
			{
				EmitAttachedProperty(gen, scope, var, type, attr);
				continue;
			}
			if (attrNs != XNamespace.None)
			{
				gen.Report(IgnoredContent, attr, $"attribute '{attr.Name.LocalName}' in XML namespace '{attrNs.NamespaceName}'");
				continue;
			}

			var name = attr.Name.LocalName;
			var value = attr.Value;

			// Event handler?
			var evt = FindEvent(type, name);
			if (evt is not null)
			{
				// A template factory is static and has no `this` to bind a handler to; event wiring belongs on the page.
				if (scope.IsTemplate) { gen.Report(IgnoredContent, attr, $"event handler '{name}' inside a template"); continue; }
				var handler = ExtractEventHandler(gen, attr, value);
				if (handler is null)
					continue;
				if (!HandlerSignatureOk(gen, handler))
					gen.Report(EventHandlerSignature, attr, gen.PageType?.ToDisplayString() ?? "the page", handler, name);
				gen.EventWires.Add($"{var}.{name} += this.{handler};");
				continue;
			}

			var prop = FindProperty(type, name);
			if (prop is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
			if (!HasAccessibleSetter(prop, gen.Compilation))
			{
				gen.Report(OutsideSubset, attr, $"Setting {type.Name}.{name}", " (it has no public setter)");
				continue;
			}

			if (!IsMarkup(value, out var inner))
			{
				// Plain text, converted for the property's type.
				if (ConvertOrReport(gen, attr, prop.Type, value, name, prop) is { } code)
					EmitSet(gen, scope, attr, type, var, prop, code);
				continue;
			}

			// {x:Bind …}
			if (TryParseXBind(inner, out var path, out var oneWay, out var twoWay, out var fallback, out var targetNull))
			{
				// FallbackValue / TargetNullValue are values of the property, converted like any other.
				var fallbackCode = fallback is null ? null : ConvertOrReport(gen, attr, prop.Type, fallback, $"{name} FallbackValue", prop);
				var targetNullCode = targetNull is null ? null : ConvertOrReport(gen, attr, prop.Type, targetNull, $"{name} TargetNullValue", prop);
				if ((fallback is not null && fallbackCode is null) || (targetNull is not null && targetNullCode is null))
					continue;
				if (scope.IsTemplate)
				{
					EmitTemplateBind(gen, scope, attr, var, prop, path, fallbackCode, targetNullCode);
					continue;
				}
				if (!TryResolvePath(gen.PageType, path, out var resultTypes, out var leafType))
				{
					gen.Report(BindPathUnresolved, attr, string.Join(".", path), gen.PageType?.ToDisplayString() ?? "the page");
					continue;
				}
				if (!gen.Compilation.ClassifyConversion(leafType, prop.Type).IsImplicit)
				{
					gen.Report(BindTypeMismatch, attr, leafType.ToDisplayString(), name, prop.Type.ToDisplayString());
					continue;
				}
				// Downgrade TwoWay to OneWay if the control has no {PropertyName}Changed event.
				var effectiveTwoWay = twoWay && FindEvent(type, prop.Name + "Changed") is not null;
				if (twoWay && !effectiveTwoWay)
					gen.Report(TwoWayNoEvent, attr, type.ToDisplayString(), prop.Name);
				gen.Bindings.Add(new Bind(var, prop.Name, path, resultTypes, leafType, oneWay, effectiveTwoWay, fallbackCode, targetNullCode));
				continue;
			}

			// {TemplateBinding source} — a OneWay wire from the templated control's property into this child.
			if (TryParseTemplateBinding(inner, out var sourceProp))
			{
				if (!scope.IsControlTemplate) { gen.Report(TemplateBindingOutsideTemplate, attr); continue; }
				EmitTemplateBinding(gen, scope, attr, var, type, prop, sourceProp);
				continue;
			}

			// {StaticResource key} / {x:Null} / {x:Static Owner.Member}
			if (TryMarkupValue(gen, scope, attr, var, inner, prop.Type, name, out var markupCode))
			{
				if (markupCode is not null)
					EmitSet(gen, scope, attr, type, var, prop, markupCode);
				continue;
			}

			gen.Report(UnsupportedMarkup, attr, value.Trim());
		}

		// Child elements flow into the type's content property ([ContentProperty], else the name convention). A
		// <X.Resources> among them makes this element a resource owner for the rest of its subtree only.
		var content = ContainerKind(type);
		var resourceDepth = scope.ResourceOwners.Count;
		List<XElement>? stateGroups = null;
		foreach (var childEl in el.Elements())
		{
			gen.Ct.ThrowIfCancellationRequested();
			if (IsIgnorable(childEl, childEl.Name.Namespace))
				continue;
			if (childEl.Name.LocalName == "VisualStateManager.VisualStateGroups")
			{
				(stateGroups ??= []).Add(childEl);
				continue;
			}
			if (childEl.Name.LocalName.Contains('.'))
			{
				HandlePropertyElement(gen, scope, var, type, childEl);
				continue;
			}
			// A child the content property can't take is reported here, not left to a C# error in generated code.
			if (content is { } expected && ContentItemType(type, expected) is { } itemType
				&& ResolveType(gen.Compilation, childEl.Name.Namespace == XNamespace.None ? Ui : childEl.Name.Namespace, childEl.Name.LocalName) is { } childType
				&& !gen.Compilation.ClassifyConversion(childType, itemType).IsImplicit)
			{
				gen.Report(WrongChildType, childEl, type.Name, childType.Name, expected.Name, itemType.Name);
				continue;
			}
			var childVar = EmitElement(gen, scope, childEl);
			if (childVar is null)
				continue;
			if (content is { } target)
				scope.Emit(target.IsCollection ? $"{var}.{target.Name}.Add({childVar});" : $"{var}.{target.Name} = {childVar};");
			else
				gen.Report(NoContentModel, childEl, typeName, childEl.Name.LocalName);
		}
		// The visual states come after the children, so their storyboards can name the parts declared below them: WinUI
		// markup puts a template root's states before its content.
		if (stateGroups is not null)
		{
			foreach (var groups in stateGroups)
				HandlePropertyElement(gen, scope, var, type, groups);
		}
		scope.ResourceOwners.RemoveRange(resourceDepth, scope.ResourceOwners.Count - resourceDepth);

		foreach (var textNode in el.Nodes().OfType<XText>())
		{
			if (!string.IsNullOrWhiteSpace(textNode.Value))
			{
				gen.Report(IgnoredContent, textNode, "text content");
				break;
			}
		}

		return var;
	}

	// --- The source table: where each element and style setter was written, for SUIsei.Diagnostics ---

	// Record where an element was written. Only a UIElement is recorded: an object element that is not one has no place in
	// the tree a diagnostics tool walks. A ControlTemplate's elements are marked as template elements, so a pick can prefer
	// the app's own markup.
	private static void RecordElementSource(Gen gen, Scope scope, XElement el, INamedTypeSymbol type, string elementVar)
	{
		if (gen.RecordableElement is not { } uiElement || !IsAssignableTo(type, uiElement) || el is not IXmlLineInfo li || !li.HasLineInfo())
			return;
		var inTemplate = scope.IsControlTemplate ? "true" : "false";
		scope.SourceRecords.Add($"global::SUIsei.Diagnostics.RecordSource({elementVar}, {Quote(gen.Path)}, {li.LineNumber}, {li.LinePosition}, {inTemplate});");
	}

	// Record where a style's setter was written; `setterExpr` reaches the Setter once the style is built.
	private static void RecordSetterSource(Gen gen, Scope scope, XElement setterEl, string setterExpr)
	{
		if (gen.RecordableElement is null || setterEl is not IXmlLineInfo li || !li.HasLineInfo())
			return;
		scope.SourceRecords.Add($"global::SUIsei.Diagnostics.RecordSource({setterExpr}, {Quote(gen.Path)}, {li.LineNumber}, {li.LinePosition});");
	}

	// A scope's RecordSource calls as one block behind the diagnostics switch, at statement indentation; empty when the
	// scope built nothing to record.
	private static string SourceRecordBlock(Scope scope)
	{
		if (scope.SourceRecords.Count == 0)
			return "";
		var sb = new StringBuilder();
		sb.AppendLine("\t\tif (global::SUIsei.Diagnostics.Enabled)");
		sb.AppendLine("\t\t{");
		foreach (var record in scope.SourceRecords)
			sb.AppendLine("\t\t\t" + record);
		sb.AppendLine("\t\t}");
		return sb.ToString();
	}

	private static bool IsAssignableTo(ITypeSymbol type, ITypeSymbol targetType)
	{
		var current = (ITypeSymbol?)type;
		while (current != null)
		{
			if (SymbolEqualityComparer.Default.Equals(current, targetType)) return true;
			current = current.BaseType;
		}
		return false;
	}
}
