using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Vysara.SourceGen;

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
/// <c>{PropertyName}Changed</c> event back to the source, converting a nullable or numeric control value for it, and reads
/// the source again after writing it, so a source that refuses or coerces a value is shown as it is. The path may instead be
/// a function, <c>prefix:Type.Method(path, …)</c>: a static method called again whenever an argument's path changes. A bool
/// shown on a <c>Visibility</c> converts as WinUI's does (true Visible). In a <c>DataTemplate</c> the root is the item
/// (<c>{x:Bind}</c> with no path is the item itself), and a OneWay or TwoWay binding is tracked by a helper made for each
/// element the template builds, attached while that element is in a live tree (<c>DataTemplate.SetBindings</c>).
/// An event attribute wires <c>el.Event += this.Handler</c>.
/// <c>{StaticResource key}</c> emits a <c>FindResource("key")</c> lookup through the markup ancestors that declared
/// resources; <c>{x:Null}</c> and <c>{x:Static}</c> resolve at compile time.
/// Converters are outside the supported subset and diagnose.
/// </para>
/// <para>
/// The source table: <c>InitializeComponent()</c> and each template factory end by recording, through
/// <c>BinaryVibrance.Vysara.Diagnostics.RecordSource</c>, the file, line and column of every element they built and every style setter,
/// with a <c>ControlTemplate</c>'s elements marked as template elements. The calls sit in one block behind
/// <c>BinaryVibrance.Vysara.Diagnostics.Enabled</c>, so they cost one check where diagnostics are off and the trimmer removes them where
/// the switch is fixed off, as under NativeAOT.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class XamlSourceGenerator : IIncrementalGenerator
{
	private const string TrackName = "XamlGenerate";

	private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
	// The Vysara UI namespace (the default xmlns of a Vysara view). Elements live in it, in the extension namespace or in
	// a `using:` CLR namespace; one in any other namespace is foreign and is never resolved as if it were a Vysara type.
	private static readonly XNamespace Ui = "https://github.com/AtomicBlom/Vysara/ui";
	// The Vysara extension namespace (conventionally `xmlns:vysara`): Vysara-only types and attached properties with no
	// WinUI equivalent, kept out of the default namespace so WinUI types keep exactly WinUI's surface.
	private static readonly XNamespace ExtensionNs = "https://github.com/AtomicBlom/Vysara/extensions";

	// Markup compatibility: mc:Ignorable names the prefixes (typically d:) whose attributes and elements are skipped.
	private static readonly XNamespace MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

	private static readonly DiagnosticDescriptor ParseError = new("VYSARA001", "Invalid XAML",
		"XAML parse error: {0}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor MissingClass = new("VYSARA002", "Missing x:Class",
		"The root element needs an x:Class attribute for the generator to emit a partial class", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownElement = new("VYSARA003", "Unknown element",
		"'{0}' is not a known Vysara type (the default XAML namespace maps BinaryVibrance.Vysara.Controls, BinaryVibrance.Vysara, BinaryVibrance.Vysara.Media, BinaryVibrance.Vysara.Shapes, BinaryVibrance.Vysara.Documents and BinaryVibrance.Vysara.Animation)", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownProperty = new("VYSARA004", "Unknown property",
		"'{0}' has no settable property '{1}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnsupportedProperty = new("VYSARA005", "Unsupported property type",
		"Property '{0}' of type '{1}' is outside the XAML subset (string / numeric / bool / enum)", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor NoContentModel = new("VYSARA006", "Element cannot hold children",
		"'{0}' has no Children/Child/Content to host the child '{1}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnsupportedMarkup = new("VYSARA007", "Unsupported markup",
		"Markup '{0}' is outside the XAML subset (supported: {{x:Bind [path | prefix:Type.Method(path, …)][, Mode=OneTime|OneWay|TwoWay][, FallbackValue=X][, TargetNullValue=Y]}}, {{StaticResource key}}, event handlers)", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BindPathUnresolved = new("VYSARA008", "Unresolved x:Bind path",
		"x:Bind path '{0}' could not be resolved on '{1}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BindTypeMismatch = new("VYSARA009", "Incompatible x:Bind types",
		"x:Bind source type '{0}' is not assignable to target property '{1}' of type '{2}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor EventHandlerSignature = new("VYSARA010", "Bad event handler",
		"'{0}' has no handler '{1}({2})' for event '{3}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidName = new("VYSARA011", "Invalid x:Name",
		"x:Name '{0}' is not usable as a field — it must be a unique, non-reserved C# identifier", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidLiteral = new("VYSARA012", "Invalid property value",
		"'{0}' is not a valid value for property '{1}' of type '{2}': expected {3}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor IgnoredContent = new("VYSARA013", "Ignored XAML content",
		"Ignored {0} — it is outside the supported XAML subset (elements, literal/enum attributes, {{x:Bind}}, {{StaticResource}}, and event handlers)", "Vysara", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor TwoWayNoEvent = new("VYSARA014", "TwoWay binding — no changed event",
		"TwoWay binding on '{0}.{1}': no '{1}Changed' event found; binding will be OneWay", "Vysara", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor BadStoryboardTarget = new("VYSARA015", "Unresolved storyboard target",
		"Storyboard.TargetName '{0}' does not name an x:Name'd element in this view", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadAnimationProperty = new("VYSARA016", "Unanimatable property",
		"Storyboard.TargetProperty '{0}' is a {1} property, which a {2} cannot animate", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadLiteralValue = new("VYSARA017", "Invalid markup value",
		"'{0}' is not a valid {1}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadSetterProperty = new("VYSARA018", "Unstylable property",
		"Setter Property '{0}' does not resolve to a dependency property '{0}Property' on '{1}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TemplateBindingOutsideTemplate = new("VYSARA019", "TemplateBinding outside a template",
		"{{TemplateBinding}} is only valid inside a ControlTemplate", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadPropertyElement = new("VYSARA020", "Unsupported property element",
		"Property element '{0}' is outside the supported XAML subset", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadPropertyPath = new("VYSARA021", "Unresolved property path",
		"Property path '{0}' does not resolve: {1}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BrushOrTransformShorthand = new("VYSARA023", "Animation shorthand for a brush or a transform",
		"Storyboard.TargetProperty '{0}' {1}; write the WinUI path '{2}'", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownExtensionMember = new("VYSARA022", "Unknown extension member",
		"'{0}' is not a member of the Vysara extension namespace (BinaryVibrance.Vysara.Extensions)", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor InvalidPathMarkup = new("VYSARA024", "Invalid path markup",
		"'{0}' is not valid path markup for property '{1}', {2}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TemplateValueOnLocal = new("VYSARA027", "Template value set as the part's own",
		"In a ControlTemplate, {0} is set through its setter as the part's local value, not as the template's: {1}", "Vysara", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor OutsideSubset = new("VYSARA025", "Outside the XAML subset",
		"{0} is outside the XAML subset{1}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor WrongChildType = new("VYSARA026", "Child of the wrong type",
		"'{0}' cannot hold a {1}: its {2} takes a {3}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TwoWayNoWriteBack = new("VYSARA028", "TwoWay binding can't write back",
		"TwoWay binding on '{0}' can't write the control's value back: {1}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadXBindFunction = new("VYSARA029", "Unresolved x:Bind function",
		"x:Bind function '{0}' does not resolve: {1}", "Vysara", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadXBind = new("VYSARA030", "Malformed x:Bind",
		"'{0}' is not an {{x:Bind}} Vysara builds: {1}", "Vysara", DiagnosticSeverity.Error, true);

	// The x: directives Vysara reads, and where: x:Name on any element (and on a visual state or group), x:FieldModifier
	// beside it, x:Key on a resource, x:Class on the root and x:DataType on a DataTemplate. x:Uid is ignored with a
	// warning, since with no localization resources the text the markup writes is the text WinUI would show without a
	// resource for it. Every other directive (x:Load, x:Phase, x:DeferLoadStrategy, x:DefaultBindMode, …) is reported
	// where it is written, not skipped, since skipping it changes what the view does: an x:Load="False" element would be
	// loaded.
	private static bool IsDirective(Gen gen, XElement el, XAttribute attr, bool isRoot = false)
	{
		if (attr.Name.Namespace != X)
			return false;
		switch (attr.Name.LocalName)
		{
			case "Uid":
				gen.Report(IgnoredContent, attr, $"x:Uid '{attr.Value}' on {el.Name.LocalName}, so its text is what the markup writes");
				return true;
			case "FieldModifier":
				if (!TryFieldModifier(attr.Value, out _))
					gen.Report(BadLiteralValue, attr, attr.Value, "x:FieldModifier (public, internal, protected, private, or Friend for internal, in any case)");
				return true;
		}
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
				"Load" or "DeferLoadStrategy" => " (every element is loaded)",
				"Phase" => " (every element is built at once)",
				"DefaultBindMode" => " (write the Mode on each {x:Bind})",
				"Class" => " (only the root element names the class)",
				"DataType" => " (only a DataTemplate takes one)",
				_ => "",
			});
		}
		return true;
	}

	// An x:FieldModifier as WinUI's XAML compiler takes one: public, private, protected, internal, or Friend (Visual Basic's
	// internal), in any case. `modifier` is the C# modifier for it.
	private static bool TryFieldModifier(string text, out string modifier)
	{
		modifier = text.Trim().ToLowerInvariant() switch
		{
			"public" => "public",
			"private" => "private",
			"protected" => "protected",
			"internal" or "friend" => "internal",
			_ => "",
		};
		return modifier.Length > 0;
	}

	// The field a named element (or a sealed root) is kept in: its x:FieldModifier, internal where it has none, and a value
	// type's default where a class starts null.
	private static string FieldDeclaration(XElement el, ITypeSymbol type, string name)
	{
		var modifier = el.Attribute(X + "FieldModifier") is { } attr && TryFieldModifier(attr.Value, out var given) ? given : "internal";
		return $"{modifier} {type.ToDisplayString()} {name} = {(type.IsValueType ? "default" : "null!")};";
	}

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var inputs = context.AdditionalTextsProvider
			.Where(static a => a.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
			.Select(static (a, ct) => new XamlInput(a.Path, a.GetText(ct)?.ToString() ?? ""));

		// Narrow: instead of combining with the raw Compilation (a new reference on every keystroke),
		// compute a fingerprint of the type surface that matters to the generator — the current
		// assembly's public and internal types (page classes, ViewModels, extension types) plus the Vysara
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

	// A property path an {x:Bind} reads from its root (the page, or a template's item): its members and the type each one
	// reads. Empty for the root itself, a template's {x:Bind} with no path.
	private sealed class BindPath(string[] segments, ITypeSymbol[] resultTypes)
	{
		public string[] Segments { get; } = segments;
		public ITypeSymbol[] ResultTypes { get; } = resultTypes;
		public int Length => Segments.Length;
		// The type of the value the whole path reads.
		public ITypeSymbol Leaf => ResultTypes[ResultTypes.Length - 1];
	}

	// What an {x:Bind} sets: a property of the element, or an attached property through its owner's static setter. Member
	// is the property, or the setter, for diagnostics that name it.
	private sealed record BindTarget(string Name, ITypeSymbol Type, ISymbol Member, string? AttachedSetter)
	{
		public static BindTarget Of(IPropertySymbol property) => new(property.Name, property.Type, property, null);
	}

	// One resolved x:Bind binding: a path, or a static function of paths; the property it sets; how its value converts on
	// the way in (a bool shown as a Visibility) and, for TwoWay, on the way back.
	private sealed class Bind
	{
		public string TargetVar { get; set; } = "";
		public string TargetProp { get; set; } = "";
		// For an attached property, the owner's static Set<Prop>, fully qualified, which the value is set through.
		public string? AttachedSetter { get; set; }
		// In a ControlTemplate, the dependency property the value goes on the part's Template rung of, fully qualified, and
		// the type it is converted to on the way, where that differs from the property's.
		public string? TemplateDp { get; set; }
		public string? TemplateCast { get; set; }
		// The target element's type, which a template's helper keeps it in a field of.
		public INamedTypeSymbol TargetElementType { get; set; } = null!;
		public BindPath? Path { get; set; }
		public IMethodSymbol? Function { get; set; }
		public BindPath[] Arguments { get; set; } = [];
		// The type of the value: the path's leaf, the function's return type, or the item for a template's {x:Bind}.
		public ITypeSymbol LeafType { get; set; } = null!;
		public bool OneWay { get; set; }  // TwoWay also pushes source → control
		public bool TwoWay { get; set; }
		public string? FallbackValue { get; set; }
		public string? TargetNullValue { get; set; }
		public bool ToVisibility { get; set; }
		// The control's value converted for the source, `{0}` standing for it.
		public string WriteBack { get; set; } = "{0}";
		// The paths whose changes the binding follows: its own, or its function's arguments.
		public IEnumerable<BindPath> Tracked => Function is not null ? Arguments : Path is { } path ? [path] : [];
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
		// BinaryVibrance.Vysara.UIElement where the referenced Vysara has the diagnostics source table to record elements into; null where
		// it has none, and then nothing is recorded.
		public INamedTypeSymbol? RecordableElement { get; } =
			compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Diagnostics") is null ? null : compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.UIElement");
		// BinaryVibrance.Vysara.FrameworkElement, whose Name an x:Name is assigned to.
		public INamedTypeSymbol? FrameworkElementType { get; } = compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.FrameworkElement");
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
		// A DataTemplate's OneWay and TwoWay {x:Bind}s, tracked against the item by a helper made for each element it builds.
		public List<Bind> TemplateBindings { get; } = [];

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
		var rootTypeName = gen.RootType?.ToDisplayString() ?? "global::BinaryVibrance.Vysara.UIElement";
		var rootIsSealed = gen.RootType?.IsSealed ?? true;

		// OneTime bindings (no Mode or Mode=OneTime) are emitted as direct assignments in InitializeComponent.
		// OneWay/TwoWay bindings need a _Bindings tracking class for INPC subscriptions.
		var oneTimeBindings = gen.Bindings.Where(b => !b.OneWay).ToList();
		var trackingBindings = gen.Bindings.Where(b => b.OneWay).ToList();
		var hasBindings = trackingBindings.Count > 0;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/> by BinaryVibrance.Vysara.SourceGen");
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
			sb.AppendLine($"\t\t{AssignStatement(b, "this", b.TargetVar)}");
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
			sb.AppendLine($"\t\tglobal::BinaryVibrance.Vysara.HotReload.Track<global::{fullClass}>(this, static __c => __c.RebuildComponent());");
		else
			sb.AppendLine($"\t\tglobal::BinaryVibrance.Vysara.HotReload.Track(this, static __c => ((global::{fullClass})__c).RebuildComponent(), static __c => (global::BinaryVibrance.Vysara.UIElement)((global::{fullClass})__c).{rootVar});");
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
		var panelType = gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Controls.Panel");
		var rootIsPanel = panelType != null && gen.RootType != null && IsAssignableTo(gen.RootType, panelType);
		if (rootIsPanel)
			sb.AppendLine(rootIsSealed ? $"\t\t{rootVar}.Children.Clear();" : "\t\tthis.Children.Clear();");
		sb.AppendLine("\t\tInitializeComponent();");
		if (rootIsSealed)
			sb.AppendLine($"\t\tglobal::BinaryVibrance.Vysara.HotReload.ReplaceRoot(__oldRoot, {rootVar});");
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
		// namespace is foreign, not resolved as if it were a Vysara type.
		var ns = el.Name.Namespace;
		var type = ResolveType(gen.Compilation, ns, el.Name.LocalName);
		if (isRoot)
			gen.RootType = type;
		if (type is null)
		{
			gen.Report(ns == ExtensionNs ? UnknownExtensionMember : UnknownElement, el, el.Name.LocalName);
			return null;
		}
		if (!isRoot && !IsConstructible(gen, type))
		{
			gen.Report(OutsideSubset, el, $"An element of {type.Name}", type.IsAbstract
				? $" ({type.Name} is abstract; write one of the types derived from it)"
				: $" (markup builds an object through a parameterless constructor the generated class can reach, which {type.Name} doesn't have; make it in code)");
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
			gen.Fields.Add(FieldDeclaration(el, type, var));
			scope.Emit($"{var} = new {typeName}();");
		}
		else if (hasName)
		{
			var = nameAttr!.Value;
			gen.Fields.Add(FieldDeclaration(el, type, var));
			scope.Emit($"{var} = new {typeName}();");
		}
		else if (NeedsField(type, el))
		{
			var = NewName(gen, "__b");
			gen.Fields.Add($"internal {typeName} {var} = {(type.IsValueType ? "default" : "null!")};");
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
				if (!HandlerSignatureOk(gen, handler, evt))
					gen.Report(EventHandlerSignature, attr, gen.PageType?.ToDisplayString() ?? "the page", handler, HandlerParameters(evt), name);
				gen.EventWires.Add($"{var}.{name} += this.{handler};");
				continue;
			}

			var prop = FindProperty(type, name);
			if (prop is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
			if (UnassignableReason(gen, prop, type, var == "this") is { } unassignable)
			{
				gen.Report(OutsideSubset, attr, $"Setting {type.Name}.{name}", unassignable);
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
			if (ParseXBind(inner) is { } spec)
			{
				if (spec.OutsideSubset)
					gen.Report(UnsupportedMarkup, attr, value.Trim());
				else if (spec.Error is { } why)
					gen.Report(BadXBind, attr, value.Trim(), why);
				else
					EmitXBind(gen, scope, attr, var, type, BindTarget.Of(prop), spec);
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

	// --- The source table: where each element and style setter was written, for BinaryVibrance.Vysara.Diagnostics ---

	// Record where an element was written. Only a UIElement is recorded: an object element that is not one has no place in
	// the tree a diagnostics tool walks. A ControlTemplate's elements are marked as template elements, so a pick can prefer
	// the app's own markup.
	private static void RecordElementSource(Gen gen, Scope scope, XElement el, INamedTypeSymbol type, string elementVar)
	{
		if (gen.RecordableElement is not { } uiElement || !IsAssignableTo(type, uiElement) || el is not IXmlLineInfo li || !li.HasLineInfo())
			return;
		var inTemplate = scope.IsControlTemplate ? "true" : "false";
		scope.SourceRecords.Add($"global::BinaryVibrance.Vysara.Diagnostics.RecordSource({elementVar}, {Quote(gen.Path)}, {li.LineNumber}, {li.LinePosition}, {inTemplate});");
	}

	// Record where a style's setter was written; `setterExpr` reaches the Setter once the style is built.
	private static void RecordSetterSource(Gen gen, Scope scope, XElement setterEl, string setterExpr)
	{
		if (gen.RecordableElement is null || setterEl is not IXmlLineInfo li || !li.HasLineInfo())
			return;
		scope.SourceRecords.Add($"global::BinaryVibrance.Vysara.Diagnostics.RecordSource({setterExpr}, {Quote(gen.Path)}, {li.LineNumber}, {li.LinePosition});");
	}

	// A scope's RecordSource calls as one block behind the diagnostics switch, at statement indentation; empty when the
	// scope built nothing to record.
	private static string SourceRecordBlock(Scope scope)
	{
		if (scope.SourceRecords.Count == 0)
			return "";
		var sb = new StringBuilder();
		sb.AppendLine("\t\tif (global::BinaryVibrance.Vysara.Diagnostics.Enabled)");
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
