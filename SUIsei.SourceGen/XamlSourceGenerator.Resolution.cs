using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SUIsei.SourceGen;

partial class XamlSourceGenerator
{
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

	// A type named in an attribute value (x:DataType, TargetType, an x:Static owner): `prefix:Name` resolves through the
	// prefix's XML namespace in scope at `context`; otherwise a full CLR name, then a name in the default namespace.
	private static INamedTypeSymbol? ResolveTypeName(Gen gen, XElement context, string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return null;
		var name = raw!.Trim();
		var colon = name.IndexOf(':');
		if (colon >= 0)
		{
			var ns = context.GetNamespaceOfPrefix(name.Substring(0, colon));
			return ns is null ? null : ResolveType(gen.Compilation, ns, name.Substring(colon + 1));
		}
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

	// The value type of a dependency-property field declared as DependencyProperty<TOwner, TValue>; null when it is
	// declared as the untyped base, whose value type the generator cannot see.
	private static ITypeSymbol? DependencyValueType(IFieldSymbol field) =>
		field.Type is INamedTypeSymbol { Name: "DependencyProperty", TypeArguments.Length: 2 } typed ? typed.TypeArguments[1] : null;

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
	// The path is a dotted member chain of any depth (e.g. Property, Object.Property, A.B.C.D); every reference-typed
	// intermediate is read null-conditionally so a null anywhere up the chain falls back rather than throwing.
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
					if (val == "OneWay") oneWay = true;
					else if (val == "OneTime") { /* default */ }
					else if (val == "TwoWay") { twoWay = true; oneWay = true; }
					else return false;
					break;
				case "FallbackValue": fallbackValue = val; break;
				case "TargetNullValue": targetNullValue = val; break;
				default: return false; // Converter= and others are unsupported
			}
		}
		path = segs;
		return true;
	}

	private static string Quote(string s) => SyntaxFactory.Literal(s).ToString();

	private static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);

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

	// The CLR namespaces the default XAML namespace maps to, searched in order, as WinUI's maps Microsoft.UI.Xaml and its
	// .Controls, .Media, .Shapes, .Documents and .Media.Animation namespaces.
	private static readonly string[] UiNamespaces =
		["SUIsei.Controls", "SUIsei", "SUIsei.Media", "SUIsei.Shapes", "SUIsei.Documents", "SUIsei.Animation"];

	// The CLR namespace the extension XAML namespace maps to: SUIsei-only attached properties and types.
	private const string ExtensionsNamespace = "SUIsei.Extensions";

	private const string UsingScheme = "using:";

	// A type named in markup under XML namespace `ns`: the default namespace searches UiNamespaces, the extension
	// namespace SUIsei.Extensions, and a `using:Some.Namespace` declaration that CLR namespace. Null otherwise.
	private static INamedTypeSymbol? ResolveType(Compilation c, XNamespace ns, string name)
	{
		if (ns == Ui || ns == XNamespace.None)
		{
			foreach (var clr in UiNamespaces)
			{
				if (c.GetTypeByMetadataName($"{clr}.{name}") is { } type)
					return type;
			}
			return null;
		}
		if (ns == ExtensionNs)
			return c.GetTypeByMetadataName($"{ExtensionsNamespace}.{name}");
		return ns.NamespaceName.StartsWith(UsingScheme, StringComparison.Ordinal)
			? c.GetTypeByMetadataName($"{ns.NamespaceName.Substring(UsingScheme.Length)}.{name}")
			: null;
	}

	// Whether markup in `ns` is ignorable where `context` sits: the markup-compatibility namespace itself, or one whose
	// prefix an mc:Ignorable on `context` or an ancestor lists (typically the d: design-time namespace).
	private static bool IsIgnorable(XElement context, XNamespace ns)
	{
		if (ns == MarkupCompatibility)
			return true;
		for (var el = context; el is not null; el = el.Parent)
		{
			if (el.Attribute(MarkupCompatibility + "Ignorable") is not { } ignorable)
				continue;
			foreach (var prefix in ignorable.Value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
			{
				if (el.GetNamespaceOfPrefix(prefix) == ns)
					return true;
			}
		}
		return false;
	}

	private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

	// A type named in the default XAML namespace.
	private static INamedTypeSymbol? ResolveType(Compilation c, string name) => ResolveType(c, Ui, name);

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

	private static ContentTarget? ContainerKind(INamedTypeSymbol type)
	{
		// 1. Honour [ContentProperty(Name = "…")] declared on the type or its nearest decorated base.
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var attr = t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ContentPropertyAttribute");
			if (attr is null)
				continue;
			var name = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "Name").Value.Value as string
				?? (attr.ConstructorArguments.Length == 1 ? attr.ConstructorArguments[0].Value as string : null);
			return !string.IsNullOrEmpty(name) && FindAnyProperty(type, name!) is { } prop
				? new ContentTarget(name!, IsAddable(prop.Type))
				: null; // declared but unresolvable — no content model
		}

		// 2. Fall back to the historical name convention for undecorated controls.
		foreach (var name in new[] { "Children", "Child", "Content" })
			for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
				if (t.GetMembers(name).OfType<IPropertySymbol>().Any())
					return new ContentTarget(name, name == "Children");
		return null;
	}

	// Any property by name (settable or get-only collection), walking the base chain.
	private static IPropertySymbol? FindAnyProperty(INamedTypeSymbol type, string name)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
		{
			var p = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
			if (p is not null)
				return p;
		}
		return null;
	}

	// A content property is a collection (we append children to it) when its type exposes a one-argument Add — on the
	// type, a base, or an implemented interface (IList<T> etc.). Otherwise it is a single slot we assign.
	private static bool IsAddable(ITypeSymbol type)
	{
		for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
			if (HasAdd(t))
				return true;
		foreach (var i in type.AllInterfaces)
			if (HasAdd(i))
				return true;
		return false;

		static bool HasAdd(INamedTypeSymbol t) =>
			t.GetMembers("Add").OfType<IMethodSymbol>().Any(m => m is { MethodKind: MethodKind.Ordinary, Parameters.Length: 1 });
	}

	// An attached property set as an attribute — `Grid.Row="0"`, or `suisei:Owner.Prop="…"` from the extension namespace:
	// a call to the owner type's static Set<Prop>(element, value), the value converted for the setter's parameter type or
	// produced by a value markup extension. Anything that does not resolve is reported as an error.
	private static void EmitAttachedProperty(Gen gen, Scope scope, string targetVar, XAttribute attr)
	{
		var ns = attr.Name.Namespace == XNamespace.None ? Ui : attr.Name.Namespace;
		var local = attr.Name.LocalName;
		var dot = local.IndexOf('.');
		var ownerName = dot > 0 ? local.Substring(0, dot) : local;
		var property = dot > 0 ? local.Substring(dot + 1) : "";
		if (FindAttachedSetter(gen, ns, ownerName, property, out var owner) is not { } setter)
		{
			if (ns == ExtensionNs)
				gen.Report(UnknownExtensionMember, attr, local);
			else if (owner is null)
				gen.Report(UnknownElement, attr, ownerName);
			else
				gen.Report(UnknownProperty, attr, owner.ToDisplayString(), property);
			return;
		}

		var valueType = setter.Parameters[1].Type;
		string? code;
		if (!IsMarkup(attr.Value, out var inner))
			code = ConvertOrReport(gen, attr, valueType, attr.Value, local);
		else if (!TryMarkupValue(gen, scope, attr, targetVar, inner, valueType, local, out code))
		{
			gen.Report(UnsupportedMarkup, attr, attr.Value.Trim());
			return;
		}
		if (code is not null)
			scope.Emit($"{Fq(owner!)}.{setter.Name}({targetVar}, {code});");
	}

	// The static Set<Prop>(target, value) an attached property `Owner.Prop` is set through, with the owner resolved in XML
	// namespace `ns`; null when either does not resolve, with `owner` null when the type itself is unknown.
	private static IMethodSymbol? FindAttachedSetter(Gen gen, XNamespace ns, string ownerName, string property, out INamedTypeSymbol? owner)
	{
		owner = property.Length > 0 ? ResolveType(gen.Compilation, ns, ownerName) : null;
		return owner?.GetMembers("Set" + property).OfType<IMethodSymbol>().FirstOrDefault(m => m is { IsStatic: true, Parameters.Length: 2 });
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
			? MakeDiagnostic(descriptor, path, text, li.LineNumber, li.LinePosition, NodeLength(node), args)
			: MakeDiagnostic(descriptor, path, text, 1, 1, args);
	}

	private static DiagnosticInfo MakeDiagnostic(DiagnosticDescriptor descriptor, string path, SourceText text, int line, int col, string[] args) =>
		MakeDiagnostic(descriptor, path, text, line, col, 0, args);

	// Build a diagnostic whose span covers <paramref name="length"/> characters of the offending token starting at
	// (line, col). A length of 0 (a parse error, or a node with no line info) degenerates to a caret at the start.
	private static DiagnosticInfo MakeDiagnostic(DiagnosticDescriptor descriptor, string path, SourceText text, int line, int col, int length, string[] args)
	{
		var startLp = new LinePosition(Math.Max(0, line - 1), Math.Max(0, col - 1));
		var start = 0;
		if (startLp.Line < text.Lines.Count)
			start = Math.Min(text.Lines[startLp.Line].Start + startLp.Character, text.Length);
		var end = Math.Min(start + Math.Max(0, length), text.Length);
		var span = TextSpan.FromBounds(start, end);
		return new DiagnosticInfo(descriptor, path, span, text.Lines.GetLinePositionSpan(span), args);
	}

	// The character width of a node's offending token, so the squiggle covers it rather than collapsing to a caret:
	// an attribute spans its whole `name="value"` form, a text run its content, and an element its (possibly prefixed)
	// tag name — the token the line info points at.
	private static int NodeLength(XObject node) => node switch
	{
		XAttribute attr => attr.ToString().Length,
		XText t => t.Value.Length,
		XElement el => el.Name.LocalName.Length + (el.GetPrefixOfNamespace(el.Name.Namespace) is { Length: > 0 } p ? p.Length + 1 : 0),
		_ => 0,
	};

	private static readonly SymbolDisplayFormat SurfaceFormat = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
		memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers
			| SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeRef,
		parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
		propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	// Fingerprint of the type surface the generator depends on: each type's non-private members with their full
	// signatures, so a changed property type or a new overload invalidates the cache as surely as a new member does.
	// Excludes method bodies and private members, so a body-only edit re-emits nothing.
	private static string ComputeFingerprint(Compilation c, CancellationToken ct)
	{
		var sb = new StringBuilder();

		// Current assembly: page classes, ViewModels, code-behind and extension types — public or internal, since the
		// generated partial reaches both. Changes here affect {x:Bind} path resolution, event-handler signature checks
		// and the types markup names.
		foreach (var type in GetVisibleTypes(c.Assembly.GlobalNamespace, includeInternal: true, ct).OrderBy(static t => t.ToDisplayString(), StringComparer.Ordinal))
			AppendSurface(sb, type, ct);

		// SUIsei assemblies: control types and their properties, used by ResolveType + FindProperty.
		foreach (var type in GetSuiseiTypes(c, ct).OrderBy(static t => t.ToDisplayString(), StringComparer.Ordinal))
			AppendSurface(sb, type, ct);

		return sb.ToString();
	}

	// One type's surface: its shape (base, interfaces, abstract/static), then each non-private member with its
	// accessibility, type, parameters and accessors. A property that gains a setter, changes type or becomes
	// internal changes the generated code, so each of those changes the fingerprint.
	private static void AppendSurface(StringBuilder sb, INamedTypeSymbol type, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		sb.Append(type.ToDisplayString(SurfaceFormat)).Append(':').Append(type.BaseType?.ToDisplayString(SurfaceFormat));
		foreach (var iface in type.AllInterfaces.Select(static i => i.ToDisplayString(SurfaceFormat)).OrderBy(static s => s, StringComparer.Ordinal))
			sb.Append(',').Append(iface);
		sb.Append(type.IsAbstract ? " abstract" : "").Append(type.IsStatic ? " static" : "").Append('{');
		foreach (var member in type.GetMembers()
			.Where(static m => m.DeclaredAccessibility is not (Accessibility.Private or Accessibility.NotApplicable))
			.Select(static m => m.ToDisplayString(SurfaceFormat))
			.OrderBy(static s => s, StringComparer.Ordinal))
			sb.Append(member).Append(';');
		sb.AppendLine("}");
	}

	private static IEnumerable<INamedTypeSymbol> GetVisibleTypes(INamespaceOrTypeSymbol root, bool includeInternal, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (root is INamedTypeSymbol ts && (ts.DeclaredAccessibility == Accessibility.Public || (includeInternal && ts.DeclaredAccessibility == Accessibility.Internal)))
			yield return ts;
		foreach (var member in root.GetMembers())
		{
			if (member is INamespaceOrTypeSymbol child)
			{
				foreach (var t in GetVisibleTypes(child, includeInternal, ct))
					yield return t;
			}
		}
	}

	private static IEnumerable<INamedTypeSymbol> GetSuiseiTypes(Compilation c, CancellationToken ct)
	{
		foreach (var module in c.Assembly.Modules)
			foreach (var asm in module.ReferencedAssemblySymbols)
			{
				if (!asm.Name.StartsWith("SUIsei", StringComparison.Ordinal)) continue;
				foreach (var t in GetVisibleTypes(asm.GlobalNamespace, includeInternal: false, ct))
					yield return t;
			}
	}
}
