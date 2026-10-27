using System.Globalization;
using System.Numerics;
using SUIsei.Animation;
using SUIsei.Markup;

namespace SUIsei;

public static partial class Diagnostics
{
	/// <summary>
	/// Convert attribute text to a value of <paramref name="type"/> as the XAML generator does at compile time: a number,
	/// <c>true</c> or <c>false</c>, an enum member by name, a string, and the structured syntaxes — colours
	/// (<c>#RRGGBB</c>, <c>#AARRGGBB</c> or a named colour), <see cref="Thickness"/> (one, two or four numbers),
	/// <see cref="Point"/>, <see cref="Vector2"/> and <see cref="Vector3"/>, <see cref="GridLength"/> (<c>Auto</c>,
	/// <c>*</c>, <c>N*</c> or a number), <see cref="TimeSpan"/> (<c>[[h:]m:]s</c>) and <see cref="RepeatBehavior"/>.
	/// A nullable type takes what its underlying type does, and <c>{x:Null}</c> where null is allowed. False, with
	/// <paramref name="value"/> null, for text the type does not accept or a type outside these.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static bool TryParseValue(Type type, string text, out object? value)
	{
		ArgumentNullException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(text);
		RequireEnabled();

		value = null;
		var underlying = Nullable.GetUnderlyingType(type);
		if (text.Trim() == "{x:Null}")
			return underlying is not null || !type.IsValueType;
		var target = underlying ?? type;
		if (!Parsable(target))
			return false;
		var s = text.Trim();

		if (target == typeof(string) || target == typeof(object))
		{
			value = text;
			return true;
		}
		if (target.IsEnum)
		{
			if (Array.IndexOf(Enum.GetNames(target), s) < 0)
				return false;
			value = Enum.Parse(target, s);
			return true;
		}
		value = target switch
		{
			_ when target == typeof(bool) => s.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
				: s.Equals("false", StringComparison.OrdinalIgnoreCase) ? false
				: null,
			_ when target == typeof(int) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
			_ when target == typeof(float) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : null,
			_ when target == typeof(double) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null,
			_ when target == typeof(HdrColor) => XamlValueSyntax.TryParseColor(s, out var a, out var r, out var g, out var b) ? HdrColor.FromRgb(r, g, b, a) : null,
			_ when target == typeof(Thickness) => XamlValueSyntax.ParseNumbers(s) switch
			{
				[var u] => new Thickness(u),
				[var h, var v] => new Thickness(h, v),
				[var l, var t, var r, var b] => new Thickness(l, t, r, b),
				_ => null,
			},
			_ when target == typeof(Point) => XamlValueSyntax.ParseNumbers(s) is [var x, var y] ? new Point(x, y) : null,
			_ when target == typeof(Vector2) => XamlValueSyntax.ParseNumbers(s) is [var x, var y] ? new Vector2(x, y) : null,
			_ when target == typeof(Vector3) => XamlValueSyntax.ParseNumbers(s) is [var x, var y, var z] ? new Vector3(x, y, z) : null,
			_ when target == typeof(GridLength) => XamlValueSyntax.TryParseGridLength(s, out var form, out var length)
				? form switch
				{
					XamlValueSyntax.GridLengthForm.Auto => GridLength.Auto,
					XamlValueSyntax.GridLengthForm.Stars => GridLength.Stars(length),
					_ => GridLength.Pixels(length),
				}
				: null,
			_ when target == typeof(TimeSpan) => XamlValueSyntax.TryParseDuration(s, out var seconds) ? TimeSpan.FromSeconds(seconds) : null,
			_ when target == typeof(RepeatBehavior) => XamlValueSyntax.TryParseRepeatBehavior(s, out var forever, out var count)
				? forever ? RepeatBehavior.Forever : RepeatBehavior.Times(count)
				: null,
			_ => null,
		};
		return value is not null;
	}

	/// <summary>Whether <see cref="TryParseValue"/> has a syntax for <paramref name="type"/>, so a value of it can be set
	/// from markup text.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static bool CanParseValue(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		RequireEnabled();
		return Parsable(Nullable.GetUnderlyingType(type) ?? type);
	}

	// The types TryParseValue converts text to: one list, so the two members cannot disagree.
	private static bool Parsable(Type target) =>
		target.IsEnum
		|| target == typeof(string) || target == typeof(object) || target == typeof(bool) || target == typeof(int)
		|| target == typeof(float) || target == typeof(double) || target == typeof(HdrColor) || target == typeof(Thickness)
		|| target == typeof(Point) || target == typeof(Vector2) || target == typeof(Vector3) || target == typeof(GridLength)
		|| target == typeof(TimeSpan) || target == typeof(RepeatBehavior);
}
