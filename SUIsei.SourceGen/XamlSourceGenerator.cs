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
/// <c>InitializeComponent()</c> that builds the tree with generated <c>new</c> (never reflection) and assigns
/// literal/enum properties resolved against the <see cref="Compilation"/> semantic model.
/// <para>
/// Compiled data + event binding: <c>{x:Bind path[, Mode=OneTime|OneWay|TwoWay][, FallbackValue=X][, TargetNullValue=Y]}</c>
/// on a property emits a typed <c>&lt;Class&gt;_Bindings</c> helper (Initialize/Update/Subscribe/StopTracking +
/// INPC tracking that switches on <c>PropertyName</c> literals). TwoWay additionally wires the control's
/// <c>{PropertyName}Changed</c> event back to the VM. An event attribute wires <c>el.Event += this.Handler</c>.
/// <c>{StaticResource key}</c> emits a <c>FindResource("key")</c> runtime lookup.
/// Function paths and converters are outside the supported subset and diagnose.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class XamlSourceGenerator : IIncrementalGenerator
{
	private const string TrackName = "XamlGenerate";

	private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
	// Elements live in the SUIsei UI namespace (the default xmlns of a SUIsei view); an element in any other
	// namespace is foreign and must not be silently resolved as if it were a SUIsei control.
	private static readonly XNamespace Ui = "https://github.com/AtomicBlom/SUIsei/ui";

	private static readonly DiagnosticDescriptor ParseError = new("SUISEI001", "Invalid XAML",
		"XAML parse error: {0}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor MissingClass = new("SUISEI002", "Missing x:Class",
		"The root element needs an x:Class attribute for the generator to emit a partial class", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor UnknownElement = new("SUISEI003", "Unknown element",
		"'{0}' is not a known SUIsei element (looked under SUIsei.Controls and SUIsei)", "SUIsei", DiagnosticSeverity.Error, true);
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
		"'{0}' is not a valid value for property '{1}' of type '{2}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor IgnoredContent = new("SUISEI013", "Ignored XAML content",
		"Ignored {0} — it is outside the supported XAML subset (elements, literal/enum attributes, {{x:Bind}}, {{StaticResource}}, and event handlers)", "SUIsei", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor TwoWayNoEvent = new("SUISEI014", "TwoWay binding — no changed event",
		"TwoWay binding on '{0}.{1}': no '{1}Changed' event found; binding will be OneWay", "SUIsei", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor BadStoryboardTarget = new("SUISEI015", "Unresolved storyboard target",
		"Storyboard.TargetName '{0}' does not name an x:Name'd element in this view", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadAnimationProperty = new("SUISEI016", "Unanimatable property",
		"Storyboard.TargetProperty '{0}' is not an animatable channel (Opacity, Background, Foreground, Fill, RenderTransform.Translate.X/Y)", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadLiteralValue = new("SUISEI017", "Invalid markup value",
		"'{0}' is not a valid {1}", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadSetterProperty = new("SUISEI018", "Unstylable property",
		"Setter Property '{0}' does not resolve to a dependency property '{0}Property' on '{1}'", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor TemplateBindingOutsideTemplate = new("SUISEI019", "TemplateBinding outside a template",
		"{{TemplateBinding}} is only valid inside a ControlTemplate", "SUIsei", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor BadPropertyElement = new("SUISEI020", "Unsupported property element",
		"Property element '{0}' is outside the supported XAML subset", "SUIsei", DiagnosticSeverity.Warning, true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var inputs = context.AdditionalTextsProvider
			.Where(static a => a.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
			.Select(static (a, ct) => new XamlInput(a.Path, a.GetText(ct)?.ToString() ?? ""));

		// Narrow: instead of combining with the raw Compilation (a new reference on every keystroke),
		// compute a fingerprint of the public type surface that matters to the generator — the current
		// assembly's public types (page classes, ViewModels) plus the SUIsei control types. Equal
		// fingerprint → the expensive Generate call is skipped even if the compilation reference changed.
		// Body-only edits, whitespace, and comment changes never affect the fingerprint.
		var results = inputs
			.Combine(context.CompilationProvider)
			.Select(static (pair, ct) =>
			{
				var (input, compilation) = pair;
				return new CompilationSnapshot(input, compilation, ComputeFingerprint(compilation, ct));
			})
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
	// AND the public type surface (fingerprint) are unchanged — ignores the Compilation reference itself,
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
		public bool IsTemplate { get; init; }
		public bool IsControlTemplate { get; init; }
		// The {x:Bind}/{TemplateBinding} data-root expression (e.g. "__data" / "__tc") and its type, for a template.
		public string DataRootExpr { get; init; } = "";
		public INamedTypeSymbol? DataRootType { get; init; }

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
		// Register for XAML Hot Reload with no-capture delegates (the registry holds only a weak reference). Inert
		// unless hot reload is active; idempotent across rebuilds.
		if (!rootIsSealed)
			sb.AppendLine($"\t\tglobal::SUIsei.HotReload.Track<global::{fullClass}>(this, static __c => __c.RebuildComponent());");
		else
			sb.AppendLine($"\t\tglobal::SUIsei.HotReload.Track(this, static __c => ((global::{fullClass})__c).RebuildComponent(), static __c => (global::SUIsei.UIElement)((global::{fullClass})__c).{rootVar});");
		sb.AppendLine("\t}");
		sb.AppendLine();
		// Re-runs construction in place for XAML Hot Reload: detach the old binding subscriptions, then rebuild the
		// tree (a fresh Root + bindings). The host swaps in the new Root.
		sb.AppendLine("\tpublic void RebuildComponent()");
		sb.AppendLine("\t{");
		if (hasBindings)
			sb.AppendLine("\t\t__bindings?.StopTracking();");
		// Only a Panel root needs Children cleared before re-running InitializeComponent; other roots
		// (ContentControl, Border, ScrollViewer, etc.) reset their content via property assignment.
		var panelType = gen.Compilation.GetTypeByMetadataName("SUIsei.Controls.Panel");
		var rootIsPanel = panelType != null && gen.RootType != null && IsAssignableTo(gen.RootType, panelType);
		if (rootIsPanel)
			sb.AppendLine(rootIsSealed ? $"\t\t{rootVar}.Children.Clear();" : "\t\tthis.Children.Clear();");
		sb.AppendLine("\t\tInitializeComponent();");
		sb.AppendLine("\t}");
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

		var type = el.Name.Namespace == Ui || el.Name.Namespace == XNamespace.None
			? ResolveType(gen.Compilation, el.Name.LocalName)
			: null;
		if (isRoot)
			gen.RootType = type;
		if (type is null) { gen.Report(UnknownElement, el, el.Name.LocalName); return null; }

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

		foreach (var attr in el.Attributes())
		{
			if (attr.IsNamespaceDeclaration || attr.Name.Namespace == X)
				continue;
			if (attr.Name.Namespace != XNamespace.None)
			{
				gen.Report(IgnoredContent, attr, $"namespaced attribute '{attr.Name.LocalName}'");
				continue;
			}
			if (attr.Name.LocalName.Contains('.'))
			{
				if (!TryEmitAttachedProperty(gen, scope, var, attr))
					gen.Report(IgnoredContent, attr, $"attached property '{attr.Name.LocalName}'");
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

			if (!IsMarkup(value, out var inner))
			{
				// Plain literal / enum / colour / thickness value.
				var prop2 = FindProperty(type, name);
				if (prop2 is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
				switch (TryAttributeValue(prop2.Type, value, out var code))
				{
					case LiteralStatus.Ok: scope.Emit($"{var}.{prop2.Name} = {code};"); break;
					case LiteralStatus.BadValue: gen.Report(InvalidLiteral, attr, value.Trim(), name, prop2.Type.ToDisplayString()); break;
					default: gen.Report(UnsupportedProperty, attr, name, prop2.Type.ToDisplayString()); break;
				}
				continue;
			}

			// {x:Bind …}
			if (TryParseXBind(inner, out var path, out var oneWay, out var twoWay, out var fallback, out var targetNull))
			{
				var prop = FindProperty(type, name);
				if (prop is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
				if (scope.IsTemplate)
				{
					EmitTemplateBind(gen, scope, attr, var, prop, path, fallback, targetNull);
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
				gen.Bindings.Add(new Bind(var, prop.Name, path, resultTypes, leafType, oneWay, effectiveTwoWay, fallback, targetNull));
				continue;
			}

			// {TemplateBinding source} — a OneWay wire from the templated control's property into this child.
			if (TryParseTemplateBinding(inner, out var sourceProp))
			{
				var prop = FindProperty(type, name);
				if (prop is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
				if (!scope.IsControlTemplate) { gen.Report(TemplateBindingOutsideTemplate, attr); continue; }
				EmitTemplateBinding(gen, scope, attr, var, prop, sourceProp);
				continue;
			}

			// {StaticResource key}
			if (TryParseStaticResource(inner, out var resKey))
			{
				var prop = FindProperty(type, name);
				if (prop is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
				scope.Emit($"{var}.{prop.Name} = {var}.FindResource(\"{resKey}\") as {AsCastType(prop.Type)};");
				continue;
			}

			gen.Report(UnsupportedMarkup, attr, value.Trim());
		}

		// Child elements flow into the type's content property ([ContentProperty], else the name convention).
		var content = ContainerKind(type);
		foreach (var childEl in el.Elements())
		{
			gen.Ct.ThrowIfCancellationRequested();
			if (childEl.Name.LocalName.Contains('.'))
			{
				HandlePropertyElement(gen, scope, var, type, childEl);
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
