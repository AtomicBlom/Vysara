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
	// A plain (non-markup) attribute value: an HdrColor (#RRGGBB / #AARRGGBB), a Thickness (uniform / "h,v" /
	// "l,t,r,b"), or the literal/enum/numeric subset. Nullable value types are unwrapped first.
	private static LiteralStatus TryAttributeValue(ITypeSymbol type, string raw, out string code)
	{
		code = "";
		var unwrapped = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;
		if (unwrapped.Name == "HdrColor")
			return TryParseColor(raw, out code) ? LiteralStatus.Ok : LiteralStatus.BadValue;
		if (unwrapped.Name == "Thickness")
			return TryThickness(raw, out code) ? LiteralStatus.Ok : LiteralStatus.BadValue;
		return TryLiteral(unwrapped, raw, out code);
	}

	// Parse a WinUI thickness: "u" (uniform), "h,v", or "l,t,r,b".
	private static bool TryThickness(string raw, out string code)
	{
		code = "";
		var parts = raw.Split(',');
		var nums = new string[parts.Length];
		for (var i = 0; i < parts.Length; i++)
		{
			var part = parts[i].Trim();
			if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
				return false;
			nums[i] = part + "f";
		}
		code = parts.Length switch
		{
			1 => $"new global::SUIsei.Thickness({nums[0]})",
			2 => $"new global::SUIsei.Thickness({nums[0]}, {nums[1]})",
			4 => $"new global::SUIsei.Thickness({nums[0]}, {nums[1]}, {nums[2]}, {nums[3]})",
			_ => "",
		};
		return code.Length > 0;
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
			return "global::SUIsei.Animation.RepeatBehavior.Forever";
		var n = s.EndsWith("x", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 1) : s;
		return double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
			? $"global::SUIsei.Animation.RepeatBehavior.Times({Fmt(count)})"
			: "global::SUIsei.Animation.RepeatBehavior.Once";
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

	// An attached property (`Grid.Row="0"`): resolve the owner type's static `Set<Prop>(element, value)` and emit the
	// call with the literal/enum value parsed against the setter's value type. Returns false (so the caller warns) if
	// the owner/setter does not resolve or the value is not a supported literal.
	private static bool TryEmitAttachedProperty(Gen gen, Scope scope, string targetVar, XAttribute attr)
	{
		var localName = attr.Name.LocalName;
		var dot = localName.IndexOf('.');
		var owner = ResolveType(gen.Compilation, localName.Substring(0, dot));
		if (owner is null)
			return false;
		var setter = owner.GetMembers("Set" + localName.Substring(dot + 1))
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m is { IsStatic: true, Parameters.Length: 2 });
		if (setter is null || TryLiteral(setter.Parameters[1].Type, attr.Value, out var code) != LiteralStatus.Ok)
			return false;
		scope.Emit($"{Fq(owner)}.{setter.Name}({targetVar}, {code});");
		return true;
	}

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

	// Fingerprint of the public type surface the generator depends on. Excludes method bodies,
	// private members, and non-SUIsei external types, so only structural changes (new property,
	// renamed type, added enum member) invalidate the incremental cache.
	private static string ComputeFingerprint(Compilation c, CancellationToken ct)
	{
		var sb = new StringBuilder();

		// Current assembly: page classes, ViewModels, code-behind types. Changes here affect
		// {x:Bind} path resolution and event-handler signature checks.
		foreach (var type in GetPublicTypes(c.Assembly.GlobalNamespace, ct).OrderBy(static t => t.ToDisplayString(), StringComparer.Ordinal))
		{
			ct.ThrowIfCancellationRequested();
			sb.Append(type.ToDisplayString()).Append('{');
			foreach (var m in type.GetMembers()
				.Where(static m => m.DeclaredAccessibility == Accessibility.Public)
				.OrderBy(static m => m.Name, StringComparer.Ordinal))
				sb.Append(m.Name).Append(';');
			sb.AppendLine("}");
		}

		// SUIsei assemblies: control types and their properties, used by ResolveType + FindProperty.
		foreach (var type in GetSuiseiTypes(c, ct).OrderBy(static t => t.ToDisplayString(), StringComparer.Ordinal))
		{
			ct.ThrowIfCancellationRequested();
			sb.Append(type.ToDisplayString()).Append('{');
			foreach (var m in type.GetMembers()
				.Where(static m => m.DeclaredAccessibility == Accessibility.Public)
				.OrderBy(static m => m.Name, StringComparer.Ordinal))
				sb.Append(m.Name).Append(';');
			sb.AppendLine("}");
		}

		return sb.ToString();
	}

	private static IEnumerable<INamedTypeSymbol> GetPublicTypes(INamespaceOrTypeSymbol root, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (root is INamedTypeSymbol ts && ts.DeclaredAccessibility == Accessibility.Public)
			yield return ts;
		foreach (var member in root.GetMembers())
			if (member is INamespaceOrTypeSymbol child)
				foreach (var t in GetPublicTypes(child, ct))
					yield return t;
	}

	private static IEnumerable<INamedTypeSymbol> GetSuiseiTypes(Compilation c, CancellationToken ct)
	{
		foreach (var module in c.Assembly.Modules)
			foreach (var asm in module.ReferencedAssemblySymbols)
			{
				if (!asm.Name.StartsWith("SUIsei", StringComparison.Ordinal)) continue;
				foreach (var t in GetPublicTypes(asm.GlobalNamespace, ct))
					yield return t;
			}
	}
}
