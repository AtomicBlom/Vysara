using System.Globalization;
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
public sealed class XamlSourceGenerator : IIncrementalGenerator
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

		var results = inputs.Combine(context.CompilationProvider)
			.Select(static (pair, ct) => Generate(pair.Left, pair.Right, ct))
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
		public string TargetVar         { get; } = targetVar;
		public string TargetProp        { get; } = targetProp;
		public string[] Path            { get; } = path;
		public ITypeSymbol[] ResultTypes { get; } = resultTypes;
		public ITypeSymbol LeafType     { get; } = leafType;
		public bool OneWay              { get; } = oneWay || twoWay;  // TwoWay also pushes VM→control
		public bool TwoWay              { get; } = twoWay;
		public string? FallbackValue    { get; } = fallbackValue;
		public string? TargetNullValue  { get; } = targetNullValue;
	}

	private sealed class Gen(Compilation compilation, string path, SourceText text, INamedTypeSymbol? pageType, CancellationToken ct)
	{
		public Compilation Compilation { get; } = compilation;
		public string Path             { get; } = path;
		public SourceText Text         { get; } = text;
		public INamedTypeSymbol? PageType { get; } = pageType;
		public CancellationToken Ct    { get; } = ct;
		public List<string> Fields     { get; } = [];
		public StringBuilder Body      { get; } = new();
		// Extra members appended to the partial class after InitializeComponent — the static template factories.
		public StringBuilder Methods   { get; } = new();
		public List<string> EventWires { get; } = [];
		public List<Bind> Bindings     { get; } = [];
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

		// OneTime bindings (no Mode or Mode=OneTime) are emitted as direct assignments in InitializeComponent.
		// OneWay/TwoWay bindings need a _Bindings tracking class for INPC subscriptions.
		var oneTimeBindings   = gen.Bindings.Where(b => !b.OneWay).ToList();
		var trackingBindings  = gen.Bindings.Where(b =>  b.OneWay).ToList();
		var hasBindings = trackingBindings.Count > 0;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/> by SUIsei.SourceGen");
		sb.AppendLine("#nullable enable");
		if (ns is not null)
			sb.AppendLine($"namespace {ns};");
		sb.AppendLine();
		sb.AppendLine($"partial class {cls}");
		sb.AppendLine("{");
		foreach (var f in gen.Fields)
			sb.AppendLine($"\t{f}");
		sb.AppendLine($"\tinternal {rootTypeName} Root = null!;");
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
		sb.AppendLine($"\t\tRoot = {rootVar};");
		if (hasBindings)
		{
			sb.AppendLine($"\t\t__bindings = new {cls}_Bindings(this);");
			sb.AppendLine("\t\t__bindings.Initialize();");
		}
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
		var hasName  = TryValidateName(gen, scope, nameAttr);

		string var;
		if (scope.IsTemplate)
		{
			// Inside a template factory every element is a local; a named one keeps its name so a storyboard /
			// VSM in the same template can target it.
			var = hasName ? nameAttr!.Value : NewName(gen, "__t");
			scope.Emit($"var {var} = new {typeName}();");
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
				gen.Report(IgnoredContent, attr, $"attached property '{attr.Name.LocalName}'");
				continue;
			}

			var name  = attr.Name.LocalName;
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
				// Plain literal / enum value.
				var prop2 = FindProperty(type, name);
				if (prop2 is null) { gen.Report(UnknownProperty, attr, typeName, name); continue; }
				switch (TryLiteral(prop2.Type, value, out var code))
				{
					case LiteralStatus.Ok:          scope.Emit($"{var}.{prop2.Name} = {code};"); break;
					case LiteralStatus.BadValue:    gen.Report(InvalidLiteral, attr, value.Trim(), name, prop2.Type.ToDisplayString()); break;
					default:                        gen.Report(UnsupportedProperty, attr, name, prop2.Type.ToDisplayString()); break;
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

		// Child elements.
		var kind = ContainerKind(type);
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
			switch (kind)
			{
				case "Children": scope.Emit($"{var}.Children.Add({childVar});"); break;
				case "Child":    scope.Emit($"{var}.Child = {childVar};"); break;
				case "Content":  scope.Emit($"{var}.Content = {childVar};"); break;
				default:         gen.Report(NoContentModel, childEl, typeName, childEl.Name.LocalName); break;
			}
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

	// --- Property elements (<Owner.Prop> children): styles, templates, storyboards, visual states ---

	private static void HandlePropertyElement(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol ownerType, XElement propEl)
	{
		var localName = propEl.Name.LocalName; // "Owner.Prop"
		var dot = localName.IndexOf('.');
		var owner = localName.Substring(0, dot);
		var prop = localName.Substring(dot + 1);

		switch (prop)
		{
			case "VisualStateGroups" when owner == "VisualStateManager":
				EmitVisualStateGroups(gen, scope, ownerVar, propEl);
				return;
			case "Resources":
				EmitResources(gen, scope, ownerVar, propEl);
				return;
			case "Template" when FindProperty(ownerType, "Template") is not null:
				EmitControlTemplateProperty(gen, scope, ownerVar, ownerType, propEl);
				return;
			case "ItemTemplate" when FindProperty(ownerType, "ItemTemplate") is not null:
				EmitDataTemplateProperty(gen, scope, ownerVar, "ItemTemplate", propEl);
				return;
			case "ContentTemplate" when FindProperty(ownerType, "ContentTemplate") is not null:
				EmitDataTemplateProperty(gen, scope, ownerVar, "ContentTemplate", propEl);
				return;
			case "ItemsPanel" when FindProperty(ownerType, "ItemsPanel") is not null:
				EmitItemsPanelProperty(gen, scope, ownerVar, propEl);
				return;
		}

		gen.Report(BadPropertyElement, propEl, localName);
	}

	private static void EmitVisualStateGroups(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var groupsVar = NewName(gen, "__vsg");
		scope.Emit($"var {groupsVar} = new global::System.Collections.Generic.List<global::SUIsei.VisualStateGroup>();");
		foreach (var groupEl in propEl.Elements())
		{
			if (groupEl.Name.LocalName != "VisualStateGroup") { gen.Report(BadPropertyElement, groupEl, groupEl.Name.LocalName); continue; }
			var groupVar = NewName(gen, "__vsgrp");
			var groupName = groupEl.Attribute("Name")?.Value ?? "";
			scope.Emit($"var {groupVar} = new global::SUIsei.VisualStateGroup {{ Name = {Quote(groupName)} }};");
			foreach (var stateEl in groupEl.Elements())
			{
				if (stateEl.Name.LocalName != "VisualState") { gen.Report(BadPropertyElement, stateEl, stateEl.Name.LocalName); continue; }
				var stateName = stateEl.Attribute("Name")?.Value ?? "";
				var sbEl = stateEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Storyboard");
				var sbExpr = sbEl is null ? "null" : EmitStoryboard(gen, scope, sbEl);
				scope.Emit($"{groupVar}.States.Add(new global::SUIsei.VisualState {{ Name = {Quote(stateName)}, Storyboard = {sbExpr} }});");
			}
			scope.Emit($"{groupsVar}.Add({groupVar});");
		}
		scope.Emit($"global::SUIsei.VisualStateManager.SetVisualStateGroups({ownerVar}, {groupsVar});");
	}

	// Build a Storyboard local and return its variable name.
	private static string EmitStoryboard(Gen gen, Scope scope, XElement sbEl)
	{
		var sbVar = NewName(gen, "__sb");
		scope.Emit($"var {sbVar} = new global::SUIsei.Storyboard();");
		foreach (var animEl in sbEl.Elements())
			EmitAnimation(gen, scope, sbVar, animEl);
		return sbVar;
	}

	private static void EmitAnimation(Gen gen, Scope scope, string sbVar, XElement animEl)
	{
		var kind = animEl.Name.LocalName;
		var typeName = kind switch
		{
			"ColorAnimation" => "global::SUIsei.ColorAnimation",
			"DoubleAnimation" => "global::SUIsei.DoubleAnimation",
			_ => null,
		};
		if (typeName is null) { gen.Report(BadPropertyElement, animEl, kind); return; }

		var animVar = NewName(gen, "__anim");
		scope.Emit($"var {animVar} = new {typeName}();");

		string? targetName = null, targetProp = null;
		foreach (var attr in animEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration)
				continue;
			switch (attr.Name.LocalName)
			{
				case "Storyboard.TargetName": targetName = attr.Value.Trim(); break;
				case "Storyboard.TargetProperty": targetProp = attr.Value.Trim(); break;
				case "From": EmitAnimationValue(gen, scope, animVar, "From", kind, attr); break;
				case "To": EmitAnimationValue(gen, scope, animVar, "To", kind, attr); break;
				case "By" when kind == "DoubleAnimation": EmitAnimationValue(gen, scope, animVar, "By", kind, attr); break;
				case "Duration":
					if (TryParseDuration(attr.Value, out var d)) scope.Emit($"{animVar}.Duration = global::System.TimeSpan.FromSeconds({Fmt(d)});");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "Duration");
					break;
				case "BeginTime":
					if (TryParseDuration(attr.Value, out var bt)) scope.Emit($"{animVar}.BeginTime = global::System.TimeSpan.FromSeconds({Fmt(bt)});");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "BeginTime");
					break;
				case "AutoReverse":
					scope.Emit($"{animVar}.AutoReverse = {(string.Equals(attr.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false")};");
					break;
				case "FillBehavior":
					scope.Emit($"{animVar}.FillBehavior = global::SUIsei.FillBehavior.{(attr.Value.Trim() == "Stop" ? "Stop" : "HoldEnd")};");
					break;
				case "RepeatBehavior":
					scope.Emit($"{animVar}.RepeatBehavior = {RepeatBehaviorExpr(attr.Value)};");
					break;
			}
		}

		if (targetName is not null)
		{
			if (!scope.Names.Contains(targetName)) gen.Report(BadStoryboardTarget, animEl, targetName);
			else scope.Emit($"{animVar}.Target = {targetName};");
		}
		if (targetProp is not null)
		{
			if (!IsAnimatableChannel(targetProp)) gen.Report(BadAnimationProperty, animEl, targetProp);
			scope.Emit($"{animVar}.TargetProperty = {Quote(targetProp)};");
		}
		scope.Emit($"{sbVar}.Children.Add({animVar});");
	}

	private static void EmitAnimationValue(Gen gen, Scope scope, string animVar, string prop, string kind, XAttribute attr)
	{
		if (kind == "ColorAnimation")
		{
			if (TryParseColor(attr.Value, out var code)) scope.Emit($"{animVar}.{prop} = {code};");
			else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "colour");
		}
		else // DoubleAnimation
		{
			if (double.TryParse(attr.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
				scope.Emit($"{animVar}.{prop} = {attr.Value.Trim()}d;");
			else
				gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "number");
		}
	}

	private static bool IsAnimatableChannel(string property) => property is
		"Opacity" or "Background" or "Foreground" or "Fill" or "RenderTransform.Translate.X" or "RenderTransform.Translate.Y";

	// --- Templates: ControlTemplate / DataTemplate / ItemsPanelTemplate factories ---

	private static void EmitControlTemplateProperty(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol ownerType, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }

		// TargetType (optional) types the data root so non-Content {TemplateBinding}s and storyboard targets resolve;
		// default to the owning control's type.
		var targetType = ResolveTypeName(gen, templateEl.Attribute("TargetType")?.Value) ?? ownerType;
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: true,
			paramDecl: "global::SUIsei.Controls.Control __tc", castLocal: null, dataRootExpr: "__tc", dataRootType: targetType);
		if (method is null)
			return;
		scope.Emit($"{ownerVar}.Template = new global::SUIsei.Controls.ControlTemplate {{ Factory = {method} }};");
	}

	private static void EmitDataTemplateProperty(Gen gen, Scope scope, string ownerVar, string targetProp, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "DataTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }

		// x:DataType types the item so the template's {x:Bind}s resolve; without it, an {x:Bind} inside diagnoses.
		var dataType = ResolveTypeName(gen, templateEl.Attribute(X + "DataType")?.Value);
		var castLocal = dataType is null ? null : $"var __data = ({Fq(dataType)})__item!;";
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: false,
			paramDecl: "object? __item", castLocal: castLocal, dataRootExpr: "__data", dataRootType: dataType);
		if (method is null)
			return;
		scope.Emit($"{ownerVar}.{targetProp} = new global::SUIsei.Controls.DataTemplate {{ Factory = {method} }};");
	}

	private static void EmitItemsPanelProperty(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var tmplEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ItemsPanelTemplate");
		var panelEl = tmplEl?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
		if (panelEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }
		var panelType = ResolveType(gen.Compilation, panelEl.Name.LocalName);
		if (panelType is null) { gen.Report(UnknownElement, panelEl, panelEl.Name.LocalName); return; }
		scope.Emit($"{ownerVar}.ItemsPanel = new global::SUIsei.Controls.ItemsPanelTemplate {{ Factory = static () => new {Fq(panelType)}() }};");
	}

	// Emit a static factory method that news the template subtree and returns its root, then return its name.
	private static string? EmitTemplateFactory(Gen gen, XElement templateEl, bool isControlTemplate, string paramDecl, string? castLocal, string dataRootExpr, INamedTypeSymbol? dataRootType)
	{
		var rootChild = templateEl.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
		if (rootChild is null) { gen.Report(BadPropertyElement, templateEl, templateEl.Name.LocalName); return null; }

		var method = NewName(gen, "__Template");
		var body = new StringBuilder();
		if (castLocal is not null)
			body.AppendLine("\t\t" + castLocal);
		var scope = new Scope
		{
			Body = body,
			Names = [],
			IsTemplate = true,
			IsControlTemplate = isControlTemplate,
			DataRootExpr = dataRootExpr,
			DataRootType = dataRootType,
		};
		var rootVar = EmitElement(gen, scope, rootChild);
		if (rootVar is null)
			return null;

		gen.Methods.AppendLine($"\tprivate static global::SUIsei.FrameworkElement {method}({paramDecl})");
		gen.Methods.AppendLine("\t{");
		gen.Methods.Append(body);
		gen.Methods.AppendLine($"\t\treturn {rootVar};");
		gen.Methods.AppendLine("\t}");
		return method;
	}

	// A template-scoped {x:Bind} — a OneTime read against the template's data root (the item is fixed for the
	// realized container's lifetime, so no INPC tracking is wired).
	private static void EmitTemplateBind(Gen gen, Scope scope, XAttribute attr, string targetVar, IPropertySymbol prop, string[] path, string? fallback, string? targetNull)
	{
		if (scope.DataRootType is null)
		{
			gen.Report(BindPathUnresolved, attr, string.Join(".", path), "the template data type (set x:DataType)");
			return;
		}
		if (!TryResolvePath(scope.DataRootType, path, out var resultTypes, out var leafType))
		{
			gen.Report(BindPathUnresolved, attr, string.Join(".", path), scope.DataRootType.ToDisplayString());
			return;
		}
		if (!gen.Compilation.ClassifyConversion(leafType, prop.Type).IsImplicit)
		{
			gen.Report(BindTypeMismatch, attr, leafType.ToDisplayString(), prop.Name, prop.Type.ToDisplayString());
			return;
		}

		var sb = new StringBuilder(scope.DataRootExpr);
		for (var i = 0; i < path.Length; i++)
			sb.Append(i == 0 ? "." : (resultTypes[i - 1].IsReferenceType ? "?." : ".")).Append(path[i]);
		scope.Emit($"{targetVar}.{prop.Name} = {CoalesceRead(sb.ToString(), leafType, fallback, targetNull)};");
	}

	// A {TemplateBinding source} wire: the templated control's property flows OneWay into a child setter.
	private static void EmitTemplateBinding(Gen gen, Scope scope, XAttribute attr, string targetVar, IPropertySymbol prop, string sourceProp)
	{
		// Content is a CLR property carrying a ContentChanged event (not a dependency property); wire via that event.
		if (sourceProp == "Content")
		{
			var host = $"((global::SUIsei.Controls.ContentControl){scope.DataRootExpr})";
			scope.Emit($"{targetVar}.{prop.Name} = {host}.Content;");
			scope.Emit($"{host}.ContentChanged += (_, _) => {targetVar}.{prop.Name} = {host}.Content;");
			return;
		}
		// Otherwise the source must be a dependency property on the templated control.
		if (scope.DataRootType is null || FindDependencyProperty(scope.DataRootType, sourceProp) is not { } dp)
		{
			gen.Report(BadAnimationProperty, attr, sourceProp);
			return;
		}
		scope.Emit($"{scope.DataRootExpr}.RegisterTemplateBinding({Fq(dp.ContainingType!)}.{dp.Name}, __v => {targetVar}.{prop.Name} = ({Fq(prop.Type)})__v!);");
	}

	// --- Resources + Style markup ---

	private static void EmitResources(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		// <X.Resources> holds keyed resources, optionally wrapped in an explicit <ResourceDictionary>.
		var items = propEl.Elements().ToList();
		if (items.Count == 1 && items[0].Name.LocalName == "ResourceDictionary")
			items = items[0].Elements().ToList();

		scope.Emit($"{ownerVar}.Resources ??= new global::SUIsei.ResourceDictionary();");
		foreach (var resEl in items)
		{
			var key = resEl.Attribute(X + "Key")?.Value;
			if (key is null) { gen.Report(BadPropertyElement, resEl, $"{resEl.Name.LocalName} (resource needs an x:Key)"); continue; }
			if (resEl.Name.LocalName != "Style") { gen.Report(BadPropertyElement, resEl, resEl.Name.LocalName); continue; }
			if (EmitStyle(gen, scope, ownerVar, resEl) is { } styleVar)
				scope.Emit($"{ownerVar}.Resources.Add({Quote(key)}, {styleVar});");
		}
	}

	private static string? EmitStyle(Gen gen, Scope scope, string ownerVar, XElement styleEl)
	{
		var targetType = ResolveTypeName(gen, styleEl.Attribute("TargetType")?.Value);
		if (targetType is null) { gen.Report(BadPropertyElement, styleEl, "Style (needs a resolvable TargetType)"); return null; }

		var styleVar = NewName(gen, "__style");
		// BasedOn="{StaticResource Key}" chains a base style, resolved from the element-tree resources at runtime.
		var basedOnInit = "";
		var basedOn = styleEl.Attribute("BasedOn")?.Value;
		if (basedOn is not null && IsMarkup(basedOn, out var inner) && TryParseStaticResource(inner, out var baseKey))
			basedOnInit = $" BasedOn = {ownerVar}.FindResource({Quote(baseKey)}) as global::SUIsei.Style,";
		scope.Emit($"var {styleVar} = new global::SUIsei.Style {{{basedOnInit} }};");

		foreach (var setterEl in styleEl.Elements())
		{
			if (setterEl.Name.LocalName != "Setter") { gen.Report(BadPropertyElement, setterEl, setterEl.Name.LocalName); continue; }
			var propName = setterEl.Attribute("Property")?.Value;
			var rawValue = setterEl.Attribute("Value")?.Value;
			if (propName is null || rawValue is null) { gen.Report(BadPropertyElement, setterEl, "Setter (needs Property and Value)"); continue; }

			// Resolve Property to the dependency-property static field <TargetType>.<Property>Property and convert the
			// value against that property's CLR type.
			var dp = FindDependencyProperty(targetType, propName);
			var clrProp = FindProperty(targetType, propName);
			if (dp is null || clrProp is null) { gen.Report(BadSetterProperty, setterEl, propName, targetType.ToDisplayString()); continue; }
			if (!TrySetterValue(clrProp.Type, rawValue, out var code))
			{
				gen.Report(InvalidLiteral, setterEl, rawValue.Trim(), propName, clrProp.Type.ToDisplayString());
				continue;
			}
			scope.Emit($"{styleVar}.Setters.Add(new global::SUIsei.Setter {{ Property = {Fq(dp.ContainingType!)}.{dp.Name}, Value = {code} }});");
		}
		return styleVar;
	}

	// A Setter value: the literal/enum subset, plus colour literals for HdrColor-typed properties (Background etc.).
	private static bool TrySetterValue(ITypeSymbol type, string raw, out string code)
	{
		var unwrapped = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;
		if (unwrapped.Name == "HdrColor")
			return TryParseColor(raw, out code);
		code = "";
		return TryLiteral(unwrapped, raw, out code) == LiteralStatus.Ok;
	}

	// Coalescing for a binding read: FallbackValue / TargetNullValue, then string/null-value-type defaults.
	private static string CoalesceRead(string read, ITypeSymbol leafType, string? fallback, string? targetNull)
	{
		if (targetNull is not null && fallback is not null) return $"({read}) ?? ({targetNull}) ?? ({fallback})";
		if (targetNull is not null) return $"({read}) ?? ({targetNull})";
		if (fallback is not null) return $"({read}) ?? ({fallback})";
		if (leafType.SpecialType == SpecialType.System_String) return $"({read}) ?? string.Empty";
		if (read.Contains("?.") && leafType.IsValueType) return $"({read}) ?? default";
		return read;
	}

	// Parse "{TemplateBinding Prop}" — inner = "TemplateBinding Prop".
	private static bool TryParseTemplateBinding(string inner, out string sourceProp)
	{
		sourceProp = "";
		var t = inner.Trim();
		if (!t.StartsWith("TemplateBinding", StringComparison.Ordinal))
			return false;
		var rest = t.Substring("TemplateBinding".Length).Trim();
		if (rest.Length == 0 || !IsIdentifier(rest))
			return false;
		sourceProp = rest;
		return true;
	}

	// Resolve an x:DataType / TargetType string to a type symbol: a fully-qualified metadata name, else (after
	// stripping any xmlns prefix) a SUIsei control/type by simple name.
	private static INamedTypeSymbol? ResolveTypeName(Gen gen, string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return null;
		var name = raw!.Trim();
		var colon = name.IndexOf(':');
		if (colon >= 0)
			name = name.Substring(colon + 1);
		return gen.Compilation.GetTypeByMetadataName(name) ?? ResolveType(gen.Compilation, name);
	}

	// A fully-qualified (global::-rooted) reference to a symbol — a type for `new`/casts or a member for a field
	// access — so generated code never depends on the consuming file's usings or risks namespace shadowing.
	private static string Fq(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

	// A target type for an `as` cast: `as` needs a nullable target, so a reference type drops its `?` annotation
	// (a value type keeps `T?`, which is itself the legal nullable form).
	private static string AsCastType(ITypeSymbol type)
	{
		var display = Fq(type);
		return type.IsReferenceType && display.EndsWith("?", StringComparison.Ordinal)
			? display.Substring(0, display.Length - 1)
			: display;
	}

	// Find a dependency-property static field by the "<Name>Property" convention (walking the base chain).
	private static IFieldSymbol? FindDependencyProperty(INamedTypeSymbol type, string name)
	{
		var fieldName = name + "Property";
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var f = t.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(f => f.IsStatic);
			if (f is not null)
				return f;
		}
		return null;
	}

	// --- The generated Bindings helper ---

	private static GeneratedSource EmitBindings(string fullClass, string? ns, string cls, List<Bind> binds)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/> by SUIsei.SourceGen");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System.Collections.Generic;");
		sb.AppendLine("using System.ComponentModel;");
		if (ns is not null)
			sb.AppendLine($"namespace {ns};");
		sb.AppendLine();
		sb.AppendLine($"internal sealed class {cls}_Bindings");
		sb.AppendLine("{");
		sb.AppendLine($"\tprivate readonly {cls} _root;");
		sb.AppendLine("\tprivate readonly List<INotifyPropertyChanged> _tracked = new();");
		sb.AppendLine($"\tpublic {cls}_Bindings({cls} root) => _root = root;");
		sb.AppendLine();

		var tracked = binds.Where(b => b.OneWay).ToList();
		var twoWayBinds = tracked.Where(b => b.TwoWay).ToList();

		// Reentrancy guards for TwoWay bindings (one bool per binding, indexed by position in `tracked`).
		for (var i = 0; i < tracked.Count; i++)
		{
			if (tracked[i].TwoWay)
				sb.AppendLine($"\tprivate bool _updating_{i};");
		}
		if (twoWayBinds.Count > 0)
			sb.AppendLine();

		// Initialize: OneTime assignments, then subscribe + push the OneWay/TwoWay values.
		sb.AppendLine("\tpublic void Initialize()");
		sb.AppendLine("\t{");
		foreach (var b in binds.Where(b => !b.OneWay))
			sb.AppendLine($"\t\t{Assignment(b)}");
		foreach (var b in twoWayBinds)
		{
			var idx = tracked.IndexOf(b);
			var changedEvt = b.TargetProp + "Changed";
			sb.AppendLine($"\t\t_root.{b.TargetVar}.{changedEvt} += On_{idx}_ControlChanged;");
		}
		sb.AppendLine("\t\tSubscribe();");
		sb.AppendLine("\t\tUpdate();");
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Update_N: push one OneWay/TwoWay value, wrapped in reentrancy guard for TwoWay.
		for (var i = 0; i < tracked.Count; i++)
		{
			var b = tracked[i];
			if (b.TwoWay)
			{
				sb.AppendLine($"\tprivate void Update_{i}()");
				sb.AppendLine("\t{");
				sb.AppendLine($"\t\tif (_updating_{i}) return;");
				sb.AppendLine($"\t\t_updating_{i} = true;");
				sb.AppendLine("\t\ttry");
				sb.AppendLine("\t\t{");
				sb.AppendLine($"\t\t\t{Assignment(b)}");
				sb.AppendLine("\t\t}");
				sb.AppendLine($"\t\tfinally {{ _updating_{i} = false; }}");
				sb.AppendLine("\t}");
			}
			else
				sb.AppendLine($"\tprivate void Update_{i}() => {Assignment(b)}");
		}
		sb.AppendLine("\tpublic void Update()");
		sb.AppendLine("\t{");
		for (var i = 0; i < tracked.Count; i++)
			sb.AppendLine($"\t\tUpdate_{i}();");
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Reverse wires for TwoWay: control → VM.
		for (var i = 0; i < tracked.Count; i++)
		{
			var b = tracked[i];
			if (!b.TwoWay)
				continue;
			// Build the write-back: _root.Path[0].Path[1]... = _root.TargetVar.TargetProp
			var lhs = WriteBackLhs(b);
			sb.AppendLine($"\tprivate void On_{i}_ControlChanged(object? sender, System.EventArgs e)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tif (_updating_{i}) return;");
			sb.AppendLine($"\t\t_updating_{i} = true;");
			sb.AppendLine("\t\ttry");
			sb.AppendLine($"\t\t{{ {lhs} = _root.{b.TargetVar}.{b.TargetProp}; }}");
			sb.AppendLine($"\t\tfinally {{ _updating_{i} = false; }}");
			sb.AppendLine("\t}");
		}

		// Subscribe to each distinct INPC source.
		var trackExprs = new List<string> { "_root" };
		foreach (var b in tracked)
		{
			if (b.Path.Length == 2)
			{
				var owner = "_root." + b.Path[0];
				if (!trackExprs.Contains(owner))
					trackExprs.Add(owner);
			}
		}

		sb.AppendLine("\tpublic void Subscribe()");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tStopTracking();");
		foreach (var expr in trackExprs)
			sb.AppendLine($"\t\tTrack({expr});");
		sb.AppendLine("\t}");
		sb.AppendLine();
		sb.AppendLine("\tpublic void StopTracking()");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tforeach (var t in _tracked) t.PropertyChanged -= OnPropertyChanged;");
		sb.AppendLine("\t\t_tracked.Clear();");
		sb.AppendLine("\t}");
		sb.AppendLine();
		sb.AppendLine("\tprivate void Track(object? o)");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tif (o is INotifyPropertyChanged inpc) { inpc.PropertyChanged += OnPropertyChanged; _tracked.Add(inpc); }");
		sb.AppendLine("\t}");
		sb.AppendLine();

		// INPC change handler.
		var intermediateNames = new HashSet<string>();
		foreach (var b in tracked)
		{
			if (b.Path.Length == 2)
				intermediateNames.Add(b.Path[0]);
		}
		var leafUpdates = new Dictionary<string, List<int>>();
		for (var i = 0; i < tracked.Count; i++)
		{
			var segs = tracked[i].Path;
			var leaf = segs[segs.Length - 1];
			if (intermediateNames.Contains(leaf))
				continue;
			if (!leafUpdates.TryGetValue(leaf, out var list))
				leafUpdates[leaf] = list = [];
			list.Add(i);
		}

		sb.AppendLine("\tprivate void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tswitch (e.PropertyName)");
		sb.AppendLine("\t\t{");
		foreach (var name in intermediateNames)
			sb.AppendLine($"\t\t\tcase \"{name}\": Subscribe(); Update(); break;");
		foreach (var kv in leafUpdates)
		{
			var calls = string.Join(" ", kv.Value.Select(i => $"Update_{i}();"));
			sb.AppendLine($"\t\t\tcase \"{kv.Key}\": {calls} break;");
		}
		sb.AppendLine("\t\t\tcase null: case \"\": Subscribe(); Update(); break;");
		sb.AppendLine("\t\t}");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		return new GeneratedSource(MakeHintName(fullClass, ".Bindings"), sb.ToString());
	}

	// "Target.Prop = <read of the page path>;" — handles FallbackValue / TargetNullValue / null-coalescing.
	private static string Assignment(Bind b)
	{
		var read = ReadExpr(b);
		string value;
		if (b.TargetNullValue is not null && b.FallbackValue is not null)
			value = $"({read}) ?? ({b.TargetNullValue}) ?? ({b.FallbackValue})";
		else if (b.TargetNullValue is not null)
			value = $"({read}) ?? ({b.TargetNullValue})";
		else if (b.FallbackValue is not null)
			value = $"({read}) ?? ({b.FallbackValue})";
		else if (b.LeafType.SpecialType == SpecialType.System_String)
			value = $"({read}) ?? string.Empty";
		else if (read.Contains("?.") && b.LeafType.IsValueType)
			value = $"({read}) ?? default";
		else
			value = read;
		return $"_root.{b.TargetVar}.{b.TargetProp} = {value};";
	}

	// OneTime assignment RHS: path accessed directly on `this` (no `_root.` prefix), with fallback coalescing.
	private static string OneTimeExpr(Bind b)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < b.Path.Length; i++)
		{
			var acc = i == 0 ? "" : (b.ResultTypes[i - 1].IsReferenceType ? "?." : ".");
			sb.Append(acc).Append(b.Path[i]);
		}
		var read = sb.ToString();
		if (b.TargetNullValue is not null && b.FallbackValue is not null)
			return $"({read}) ?? ({b.TargetNullValue}) ?? ({b.FallbackValue})";
		if (b.TargetNullValue is not null)
			return $"({read}) ?? ({b.TargetNullValue})";
		if (b.FallbackValue is not null)
			return $"({read}) ?? ({b.FallbackValue})";
		if (b.LeafType.SpecialType == SpecialType.System_String)
			return $"({read}) ?? string.Empty";
		if (read.Contains("?.") && b.LeafType.IsValueType)
			return $"({read}) ?? default";
		return read;
	}

	// Build the left-hand-side write-back expression for TwoWay: _root.A.B... (up to the leaf's owner).
	private static string WriteBackLhs(Bind b)
	{
		if (b.Path.Length == 1)
			return $"_root.{b.Path[0]}";
		var sb = new StringBuilder("_root");
		for (var i = 0; i < b.Path.Length - 1; i++)
			sb.Append('.').Append(b.Path[i]);
		sb.Append('.').Append(b.Path[b.Path.Length - 1]);
		return sb.ToString();
	}

	private static string ReadExpr(Bind b)
	{
		var sb = new StringBuilder("_root");
		for (var i = 0; i < b.Path.Length; i++)
		{
			var acc = i == 0 ? "." : (b.ResultTypes[i - 1].IsReferenceType ? "?." : ".");
			sb.Append(acc).Append(b.Path[i]);
		}
		return sb.ToString();
	}

	// --- Resolution helpers ---

	private static bool NeedsField(INamedTypeSymbol type, XElement el)
	{
		foreach (var attr in el.Attributes())
		{
			if (attr.IsNamespaceDeclaration || attr.Name.Namespace == X || attr.Name.Namespace != XNamespace.None || attr.Name.LocalName.Contains('.'))
				continue;
			if (FindEvent(type, attr.Name.LocalName) is not null)
				return true;
			if (IsMarkup(attr.Value, out var inner) && inner.TrimStart().StartsWith("x:Bind"))
				return true;
		}
		return false;
	}

	private static bool IsMarkup(string value, out string inner)
	{
		var t = value.Trim();
		if (t.Length >= 2 && t[0] == '{' && t[t.Length - 1] == '}')
		{
			inner = t.Substring(1, t.Length - 2).Trim();
			return true;
		}
		inner = "";
		return false;
	}

	// Parse "{StaticResource key}" — inner = "StaticResource key".
	private static bool TryParseStaticResource(string inner, out string key)
	{
		key = "";
		var trimmed = inner.Trim();
		if (!trimmed.StartsWith("StaticResource", StringComparison.Ordinal))
			return false;
		var rest = trimmed.Substring("StaticResource".Length).Trim();
		if (rest.Length == 0 || !IsIdentifier(rest))
			return false;
		key = rest;
		return true;
	}

	// Parse "x:Bind path[, Mode=OneTime|OneWay|TwoWay][, FallbackValue=X][, TargetNullValue=Y]".
	private static bool TryParseXBind(string inner, out string[] path, out bool oneWay, out bool twoWay, out string? fallbackValue, out string? targetNullValue)
	{
		path = [];
		oneWay = false;
		twoWay = false;
		fallbackValue = null;
		targetNullValue = null;

		var parts = inner.Split(',');
		var head = parts[0].Trim();
		if (!head.StartsWith("x:Bind"))
			return false;
		var p = head.Substring("x:Bind".Length).Trim();
		if (p.Length == 0 || p.Contains('(') || p.Contains(' '))
			return false;
		var segs = p.Split('.');
		foreach (var s in segs)
		{
			if (!IsIdentifier(s))
				return false;
		}
		if (segs.Length > 2)
			return false;

		for (var i = 1; i < parts.Length; i++)
		{
			var eqIdx = parts[i].IndexOf('=');
			if (eqIdx < 0)
				return false;
			var key = parts[i].Substring(0, eqIdx).Trim();
			var val = parts[i].Substring(eqIdx + 1).Trim();
			switch (key)
			{
				case "Mode":
					if (val == "OneWay")        oneWay = true;
					else if (val == "OneTime")  { /* default */ }
					else if (val == "TwoWay")   { twoWay = true; oneWay = true; }
					else                        return false;
					break;
				case "FallbackValue":    fallbackValue   = val; break;
				case "TargetNullValue":  targetNullValue = val; break;
				default:                 return false; // Converter= and others are unsupported
			}
		}
		path = segs;
		return true;
	}

	private static string Quote(string s) => SyntaxFactory.Literal(s).ToString();

	private static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);

	// "Forever" or "Nx" (a finite iteration count) → a RepeatBehavior expression.
	private static string RepeatBehaviorExpr(string raw)
	{
		var s = raw.Trim();
		if (string.Equals(s, "Forever", StringComparison.OrdinalIgnoreCase))
			return "global::SUIsei.RepeatBehavior.Forever";
		var n = s.EndsWith("x", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 1) : s;
		return double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
			? $"global::SUIsei.RepeatBehavior.Times({Fmt(count)})"
			: "global::SUIsei.RepeatBehavior.Once";
	}

	// "#RRGGBB" / "#AARRGGBB" → HdrColor.FromRgb(...). Returns false for anything else.
	private static bool TryParseColor(string raw, out string code)
	{
		code = "";
		var s = raw.Trim();
		if (s.StartsWith("#", StringComparison.Ordinal))
			s = s.Substring(1);
		if (s.Length == 6 && IsHex(s))
		{
			code = $"global::SUIsei.HdrColor.FromRgb({Hex(s, 0)}, {Hex(s, 2)}, {Hex(s, 4)})";
			return true;
		}
		if (s.Length == 8 && IsHex(s))
		{
			code = $"global::SUIsei.HdrColor.FromRgb({Hex(s, 2)}, {Hex(s, 4)}, {Hex(s, 6)}, {Hex(s, 0)})";
			return true;
		}
		return false;
	}

	private static bool IsHex(string s)
	{
		foreach (var c in s)
			if (!Uri.IsHexDigit(c))
				return false;
		return true;
	}

	private static int Hex(string s, int i) => Convert.ToInt32(s.Substring(i, 2), 16);

	// "h:m:s(.fff)" / "m:s" / "s" → total seconds.
	private static bool TryParseDuration(string raw, out double seconds)
	{
		seconds = 0;
		var parts = raw.Trim().Split(':');
		try
		{
			switch (parts.Length)
			{
				case 3:
					seconds = int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
						+ int.Parse(parts[1], CultureInfo.InvariantCulture) * 60
						+ double.Parse(parts[2], CultureInfo.InvariantCulture);
					return true;
				case 2:
					seconds = int.Parse(parts[0], CultureInfo.InvariantCulture) * 60
						+ double.Parse(parts[1], CultureInfo.InvariantCulture);
					return true;
				case 1:
					seconds = double.Parse(parts[0], CultureInfo.InvariantCulture);
					return true;
				default:
					return false;
			}
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static bool IsIdentifier(string s)
	{
		if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_'))
			return false;
		foreach (var c in s)
		{
			if (!(char.IsLetterOrDigit(c) || c == '_'))
				return false;
		}
		return true;
	}

	private static bool TryResolvePath(INamedTypeSymbol? pageType, string[] path, out ITypeSymbol[] resultTypes, out ITypeSymbol leafType)
	{
		resultTypes = [];
		leafType = null!;
		if (pageType is null)
			return false;
		var types = new ITypeSymbol[path.Length];
		ITypeSymbol cur = pageType;
		for (var i = 0; i < path.Length; i++)
		{
			if (cur is not INamedTypeSymbol named || FindReadable(named, path[i]) is not { } memberType)
				return false;
			types[i] = memberType;
			cur = memberType;
		}
		resultTypes = types;
		leafType = types[types.Length - 1];
		return true;
	}

	private static ITypeSymbol? FindReadable(INamedTypeSymbol type, string name)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var p = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => p.GetMethod is not null);
			if (p is not null)
				return p.Type;
			var f = t.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault();
			if (f is not null)
				return f.Type;
		}
		return null;
	}

	private static IEventSymbol? FindEvent(INamedTypeSymbol type, string name)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var e = t.GetMembers(name).OfType<IEventSymbol>().FirstOrDefault();
			if (e is not null)
				return e;
		}
		return null;
	}

	private static string? ExtractEventHandler(Gen gen, XAttribute attr, string value)
	{
		if (!IsMarkup(value, out var inner))
			return value.Trim();
		if (inner.TrimStart().StartsWith("x:Bind"))
		{
			var h = inner.Substring(inner.IndexOf("x:Bind", StringComparison.Ordinal) + "x:Bind".Length).Trim();
			if (IsIdentifier(h))
				return h;
		}
		gen.Report(UnsupportedMarkup, attr, value.Trim());
		return null;
	}

	private static bool HandlerSignatureOk(Gen gen, string handler)
	{
		if (gen.PageType is null)
			return true;
		for (INamedTypeSymbol? t = gen.PageType; t is not null; t = t.BaseType)
		{
			foreach (var m in t.GetMembers(handler).OfType<IMethodSymbol>())
			{
				if (m.ReturnsVoid && m.Parameters.Length == 2 &&
					m.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
					m.Parameters[1].Type.Name == "RoutedEventArgs")
					return true;
			}
		}
		return false;
	}

	private static INamedTypeSymbol? ResolveType(Compilation c, string localName) =>
		c.GetTypeByMetadataName($"SUIsei.Controls.{localName}") ?? c.GetTypeByMetadataName($"SUIsei.{localName}");

	private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var p = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => p.SetMethod is not null);
			if (p is not null)
				return p;
		}
		return null;
	}

	private static string? ContainerKind(INamedTypeSymbol type)
	{
		foreach (var name in new[] { "Children", "Child", "Content" })
		{
			for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
			{
				if (t.GetMembers(name).OfType<IPropertySymbol>().Any())
					return name;
			}
		}
		return null;
	}

	private enum LiteralStatus { Ok, BadValue, UnsupportedType }

	private static LiteralStatus TryLiteral(ITypeSymbol type, string raw, out string code)
	{
		code = "";
		var value = raw.Trim();
		if (type.TypeKind == TypeKind.Enum)
		{
			if (!type.GetMembers(value).OfType<IFieldSymbol>().Any())
				return LiteralStatus.BadValue;
			code = $"{type.ToDisplayString()}.{value}";
			return LiteralStatus.Ok;
		}
		switch (type.SpecialType)
		{
			case SpecialType.System_String:
			case SpecialType.System_Object:
				code = SyntaxFactory.Literal(raw).ToString();
				return LiteralStatus.Ok;
			case SpecialType.System_Boolean:
				if (string.Equals(value, "true",  StringComparison.OrdinalIgnoreCase)) { code = "true";  return LiteralStatus.Ok; }
				if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { code = "false"; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Single:
				if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { code = $"{value}f"; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Double:
				if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { code = $"{value}d"; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Int32:
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { code = value; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			default:
				return LiteralStatus.UnsupportedType;
		}
	}

	private static bool TryValidateName(Gen gen, Scope scope, XAttribute? nameAttr)
	{
		if (nameAttr is null)
			return false;
		var name = nameAttr.Value;
		// The name becomes a field (page) or a local (template); both must be a unique, non-reserved C# identifier
		// within their scope. The page scope's set is the generator's global set (also seeded with the reserved Root).
		if (IsValidFieldIdentifier(name) && scope.Names.Add(name))
			return true;
		gen.Report(InvalidName, nameAttr, name);
		return false;
	}

	private static bool IsValidFieldIdentifier(string name) =>
		SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;

	private static string NewName(Gen gen, string prefix)
	{
		string name;
		do name = $"{prefix}{gen.Counter++}"; while (!gen.UsedNames.Add(name));
		return name;
	}

	private static (string? ns, string cls) SplitClass(string full)
	{
		var dot = full.LastIndexOf('.');
		return dot < 0 ? (null, full) : (full.Substring(0, dot), full.Substring(dot + 1));
	}

	private static string MakeHintName(string fullClass, string suffix)
	{
		var sb = new StringBuilder(fullClass.Length + suffix.Length + 5);
		foreach (var c in fullClass)
			sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');
		return sb.Append(suffix).Append(".g.cs").ToString();
	}

	private static DiagnosticInfo MakeDiagnostic(DiagnosticDescriptor descriptor, string path, SourceText text, XObject node, string[] args)
	{
		var li = (IXmlLineInfo)node;
		return li.HasLineInfo()
			? MakeDiagnostic(descriptor, path, text, li.LineNumber, li.LinePosition, args)
			: MakeDiagnostic(descriptor, path, text, 1, 1, args);
	}

	private static DiagnosticInfo MakeDiagnostic(DiagnosticDescriptor descriptor, string path, SourceText text, int line, int col, string[] args)
	{
		var lp = new LinePosition(Math.Max(0, line - 1), Math.Max(0, col - 1));
		var start = 0;
		if (lp.Line < text.Lines.Count)
			start = Math.Min(text.Lines[lp.Line].Start + lp.Character, text.Length);
		return new DiagnosticInfo(descriptor, path, new TextSpan(start, 0), new LinePositionSpan(lp, lp), args);
	}
}
