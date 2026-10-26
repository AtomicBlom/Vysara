using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SUIsei.Markup;

namespace SUIsei.SourceGen;

partial class XamlSourceGenerator
{
	// --- Values: XAML text and value markup → a C# expression of the target type ---

	private delegate bool Converter(string raw, out string code);

	// The compile-time converters for structured types, keyed by the type's full name, each with the syntax it accepts
	// (for the diagnostic). Every value position — attributes, setters, attached properties, property-element text,
	// animation values, FallbackValue/TargetNullValue — converts through this table, so adding a type is one entry here.
	// Enums, numbers, bool and string are handled after it, by type kind; Nullable<T> converts as T.
	private static readonly Dictionary<string, (Converter Convert, string Syntax)> Converters = new()
	{
		["SUIsei.HdrColor"] = (ConvertColor, "#RRGGBB, #AARRGGBB or a named colour"),
		["SUIsei.Thickness"] = (ConvertThickness, "one, two or four numbers"),
		["SUIsei.Point"] = (ConvertPoint, "two numbers, 'x,y'"),
		["System.Numerics.Vector2"] = (ConvertVector2, "two numbers, 'x,y'"),
		["System.Numerics.Vector3"] = (ConvertVector3, "three numbers, 'x,y,z'"),
		["SUIsei.GridLength"] = (ConvertGridLength, "Auto, *, N* or a number"),
		["System.TimeSpan"] = (ConvertTimeSpan, "[[h:]m:]s"),
		["SUIsei.Animation.RepeatBehavior"] = (ConvertRepeatBehavior, "Forever, or a count such as 3x"),
	};

	// Convert attribute text for a value of `type`. `expected` names the accepted syntax for a diagnostic.
	private static LiteralStatus TryConvertValue(ITypeSymbol type, string raw, out string code, out string expected)
	{
		var unwrapped = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;
		if (Converters.TryGetValue(unwrapped.ToDisplayString(), out var converter))
		{
			expected = converter.Syntax;
			return converter.Convert(raw, out code) ? LiteralStatus.Ok : LiteralStatus.BadValue;
		}
		expected = unwrapped.TypeKind == TypeKind.Enum
			? $"a member of {unwrapped.Name}"
			: unwrapped.SpecialType switch
			{
				SpecialType.System_Boolean => "true or false",
				SpecialType.System_Int32 => "a whole number",
				SpecialType.System_Single or SpecialType.System_Double => "a number",
				_ => "text",
			};
		return TryLiteral(unwrapped, raw, out code);
	}

	// The literal subset by type kind: an enum member, a string, bool, int, float or double.
	private static LiteralStatus TryLiteral(ITypeSymbol type, string raw, out string code)
	{
		code = "";
		var value = raw.Trim();
		if (type.TypeKind == TypeKind.Enum)
		{
			if (!type.GetMembers(value).OfType<IFieldSymbol>().Any())
				return LiteralStatus.BadValue;
			code = $"{Fq(type)}.{value}";
			return LiteralStatus.Ok;
		}
		switch (type.SpecialType)
		{
			case SpecialType.System_String:
			case SpecialType.System_Object:
				code = SyntaxFactory.Literal(raw).ToString();
				return LiteralStatus.Ok;
			case SpecialType.System_Boolean:
				if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { code = "true"; return LiteralStatus.Ok; }
				if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { code = "false"; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Single:
				if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && IsFinite(f)) { code = F(f); return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Double:
				if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && IsFinite(d)) { code = Fmt(d) + "d"; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			case SpecialType.System_Int32:
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { code = value; return LiteralStatus.Ok; }
				return LiteralStatus.BadValue;
			default:
				return LiteralStatus.UnsupportedType;
		}
	}

	// Convert, reporting a bad or unsupported value against `node`; null when it was reported.
	private static string? ConvertOrReport(Gen gen, XObject node, ITypeSymbol type, string raw, string propertyName)
	{
		switch (TryConvertValue(type, raw, out var code, out var expected))
		{
			case LiteralStatus.Ok:
				return code;
			case LiteralStatus.BadValue:
				gen.Report(InvalidLiteral, node, raw.Trim(), propertyName, type.ToDisplayString(), expected);
				return null;
			default:
				gen.Report(UnsupportedProperty, node, propertyName, type.ToDisplayString());
				return null;
		}
	}

	private static bool ConvertColor(string raw, out string code)
	{
		code = "";
		if (!XamlValueSyntax.TryParseColor(raw, out var a, out var r, out var g, out var b))
			return false;
		code = a == 255
			? $"global::SUIsei.HdrColor.FromRgb({r}, {g}, {b})"
			: $"global::SUIsei.HdrColor.FromRgb({r}, {g}, {b}, {a})";
		return true;
	}

	// WinUI thickness: "u" (uniform), "h,v", or "l,t,r,b".
	private static bool ConvertThickness(string raw, out string code)
	{
		var n = XamlValueSyntax.ParseNumbers(raw);
		code = n?.Length switch
		{
			1 => $"new global::SUIsei.Thickness({F(n[0])})",
			2 => $"new global::SUIsei.Thickness({F(n[0])}, {F(n[1])})",
			4 => $"new global::SUIsei.Thickness({F(n[0])}, {F(n[1])}, {F(n[2])}, {F(n[3])})",
			_ => "",
		};
		return code.Length > 0;
	}

	private static bool ConvertPoint(string raw, out string code)
	{
		var n = XamlValueSyntax.ParseNumbers(raw);
		code = n?.Length == 2 ? $"new global::SUIsei.Point({F(n[0])}, {F(n[1])})" : "";
		return code.Length > 0;
	}

	private static bool ConvertVector2(string raw, out string code)
	{
		var n = XamlValueSyntax.ParseNumbers(raw);
		code = n?.Length == 2 ? $"new global::System.Numerics.Vector2({F(n[0])}, {F(n[1])})" : "";
		return code.Length > 0;
	}

	private static bool ConvertVector3(string raw, out string code)
	{
		var n = XamlValueSyntax.ParseNumbers(raw);
		code = n?.Length == 3 ? $"new global::System.Numerics.Vector3({F(n[0])}, {F(n[1])}, {F(n[2])})" : "";
		return code.Length > 0;
	}

	// A WinUI grid length: "Auto", "*" or "N*" (star weight), or a pixel number.
	private static bool ConvertGridLength(string raw, out string code)
	{
		var value = raw.Trim();
		code = "";
		if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
			code = "global::SUIsei.GridLength.Auto";
		else if (value == "*")
			code = "global::SUIsei.GridLength.Star";
		else if (value.EndsWith("*", StringComparison.Ordinal))
		{
			if (XamlValueSyntax.ParseNumbers(value.Substring(0, value.Length - 1)) is { Length: 1 } weight)
				code = $"global::SUIsei.GridLength.Stars({F(weight[0])})";
		}
		else if (XamlValueSyntax.ParseNumbers(value) is { Length: 1 } pixels)
			code = $"global::SUIsei.GridLength.Pixels({F(pixels[0])})";
		return code.Length > 0;
	}

	private static bool ConvertTimeSpan(string raw, out string code)
	{
		code = TryParseDuration(raw, out var seconds) ? $"global::System.TimeSpan.FromSeconds({Fmt(seconds)})" : "";
		return code.Length > 0;
	}

	// "Forever", or an iteration count with an optional x ("3x", "0.5x").
	private static bool ConvertRepeatBehavior(string raw, out string code)
	{
		var s = raw.Trim();
		if (string.Equals(s, "Forever", StringComparison.OrdinalIgnoreCase))
		{
			code = "global::SUIsei.Animation.RepeatBehavior.Forever";
			return true;
		}
		var count = s.EndsWith("x", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 1) : s;
		code = double.TryParse(count, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && IsFinite(n) && n > 0
			? $"global::SUIsei.Animation.RepeatBehavior.Times({Fmt(n)})"
			: "";
		return code.Length > 0;
	}

	// An attribute of a plain object the generator builds (an animation, a keyframe): the object's settable property of
	// that name, the text converted for its type. Unknown names and bad values are reported.
	private static void EmitObjectAttribute(Gen gen, Scope scope, string objectVar, INamedTypeSymbol type, XAttribute attr)
	{
		var name = attr.Name.LocalName;
		if (FindProperty(type, name) is not { } prop)
			gen.Report(UnknownProperty, attr, type.ToDisplayString(), name);
		else if (IsMarkup(attr.Value, out _))
			gen.Report(UnsupportedMarkup, attr, attr.Value.Trim());
		else if (ConvertOrReport(gen, attr, prop.Type, attr.Value, name) is { } code)
			scope.Emit($"{objectVar}.{prop.Name} = {code};");
	}

	// A float as a C# literal; non-finite values are rejected before they get here.
	private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";

	private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

	// --- Value markup: {StaticResource}, {x:Null}, {x:Static} ---

	// A markup extension that stands for a value of `targetType`. Returns false when `inner` is not one of these (the
	// caller handles bindings); otherwise true, with `code` null if it was reported as not fitting the target.
	// `ownerVar` is the element a resource lookup starts from.
	private static bool TryMarkupValue(Gen gen, Scope scope, XAttribute attr, string ownerVar, string inner, ITypeSymbol targetType, string propertyName, out string? code)
	{
		code = null;
		if (TryParseStaticResource(inner, out var key))
		{
			code = targetType.IsValueType && targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
				? $"({Fq(targetType)})({ResourceLookup(scope, ownerVar, key)})!"
				: $"({ResourceLookup(scope, ownerVar, key)}) as {AsCastType(targetType)}";
			return true;
		}

		if (inner == "x:Null")
		{
			if (targetType.IsValueType && targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
				gen.Report(InvalidLiteral, attr, "{x:Null}", propertyName, targetType.ToDisplayString(), "a value; the property cannot hold null");
			else
				code = "null";
			return true;
		}

		if (inner.StartsWith("x:Static ", StringComparison.Ordinal))
		{
			var reference = inner.Substring("x:Static ".Length).Trim();
			if (ResolveStaticMember(gen, attr.Parent!, reference) is not { } member)
			{
				gen.Report(BadLiteralValue, attr, reference, "x:Static reference: it names no accessible static field, property or enum member");
				return true;
			}
			var memberType = member switch { IFieldSymbol f => f.Type, IPropertySymbol p => p.Type, _ => null };
			if (memberType is null || !gen.Compilation.ClassifyConversion(memberType, targetType).IsImplicit)
			{
				gen.Report(InvalidLiteral, attr, $"{{{inner}}}", propertyName, targetType.ToDisplayString(), $"a {targetType.Name}; '{reference}' is a {memberType?.Name ?? "member"}");
				return true;
			}
			code = $"{Fq(member.ContainingType)}.{member.Name}";
			return true;
		}

		return false;
	}

	// A {StaticResource key} lookup made while the tree is being built. No element is parented until its subtree is done,
	// so `fromVar`'s own Parent chain does not reach the markup ancestors yet: the lookup asks `fromVar`, then each markup
	// ancestor whose resources are already emitted, innermost first. Each FindResource also walks that element's runtime
	// Parent chain, which is how a template reaches the resources above the control it templates.
	private static string ResourceLookup(Scope scope, string fromVar, string key)
	{
		var sb = new StringBuilder($"{fromVar}.FindResource({Quote(key)})");
		for (var i = scope.ResourceOwners.Count - 1; i >= 0; i--)
		{
			if (scope.ResourceOwners[i] != fromVar)
				sb.Append($" ?? {scope.ResourceOwners[i]}.FindResource({Quote(key)})");
		}
		return sb.ToString();
	}

	// "Owner.Member", "prefix:Owner.Member" or "Namespace.Owner.Member" → a static field, property or enum member that
	// the generated code, which is part of the page class, can access.
	private static ISymbol? ResolveStaticMember(Gen gen, XElement context, string reference)
	{
		var dot = reference.LastIndexOf('.');
		if (dot <= 0)
			return null;
		var owner = ResolveTypeName(gen, context, reference.Substring(0, dot));
		var name = reference.Substring(dot + 1);
		ISymbol within = (ISymbol?)gen.PageType ?? gen.Compilation.Assembly;
		return owner?.GetMembers(name).FirstOrDefault(m =>
			m is IFieldSymbol or IPropertySymbol && m.IsStatic && gen.Compilation.IsSymbolAccessibleWithin(m, within));
	}
}
