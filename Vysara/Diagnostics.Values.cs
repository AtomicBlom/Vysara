using System.Globalization;
using System.Numerics;
using Vysara.Animation;
using Vysara.Markup;
using Vysara.Media;

namespace Vysara;

public static partial class Diagnostics
{
	/// <summary>
	/// Convert attribute text to a value of <paramref name="type"/> as the XAML generator does at compile time: a number,
	/// <c>true</c> or <c>false</c>, an enum member by name, a string, and the structured syntaxes — colours
	/// (<c>#RRGGBB</c>, <c>#AARRGGBB</c> or a named colour), a <see cref="Brush"/> from a colour (a new
	/// <see cref="SolidColorBrush"/> of it), <see cref="Thickness"/> (one, two or four numbers), <see cref="CornerRadius"/>
	/// (one or four numbers), <see cref="Point"/>, <see cref="Size"/>, <see cref="Vector2"/> and
	/// <see cref="Vector3"/>, <see cref="Rect"/> (<c>x,y,width,height</c>), <see cref="Matrix3x2"/> (six numbers,
	/// <c>M11,M12,M21,M22,OffsetX,OffsetY</c>), a <see cref="Geometry"/> from path markup, a <see cref="PointCollection"/>
	/// (pairs of numbers) and a <see cref="DoubleCollection"/> (numbers), each empty for empty text, <see cref="GridLength"/>
	/// (<c>Auto</c>, <c>*</c>, <c>N*</c> or a number), <see cref="TimeSpan"/> (WinUI's grammar, as <c>0:0:0.25</c>, with the
	/// forms the generator refuses refused here too) and <see cref="RepeatBehavior"/> (<c>Forever</c> or a count such as
	/// <c>3x</c>). A nullable type takes what its underlying type does, and <c>{x:Null}</c> where null is allowed. False, with
	/// <paramref name="value"/> null, for text the type does not accept or a type outside these. Text one property alone
	/// takes, such as <c>Auto</c> for a <see cref="FrameworkElement.Width"/>, needs the property:
	/// <see cref="TryParseValue(DependencyProperty, string, out object?)"/>.
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
			// A brush from a colour, as the generator converts one: a SolidColorBrush of it, made for this use alone.
			_ when target == typeof(Brush) || target == typeof(SolidColorBrush) =>
				XamlValueSyntax.TryParseColor(s, out var a, out var r, out var g, out var b) ? new SolidColorBrush(HdrColor.FromRgb(r, g, b, a)) : null,
			_ when target == typeof(Thickness) => XamlValueSyntax.ParseNumbers(s) switch
			{
				[var u] => new Thickness(u),
				[var h, var v] => new Thickness(h, v),
				[var l, var t, var r, var b] => new Thickness(l, t, r, b),
				_ => null,
			},
			_ when target == typeof(CornerRadius) => XamlValueSyntax.ParseNumbers(s) switch
			{
				[var u] => new CornerRadius(u),
				[var tl, var tr, var br, var bl] => new CornerRadius(tl, tr, br, bl),
				_ => null,
			},
			_ when target == typeof(Size) => XamlValueSyntax.ParseNumbers(s) is [var w, var h] && w >= 0 && h >= 0 ? new Size(w, h) : null,
			_ when target == typeof(Rect) => XamlValueSyntax.ParseNumbers(s) is [var x, var y, var w, var h] && w >= 0 && h >= 0 ? new Rect(x, y, w, h) : null,
			_ when target == typeof(Geometry) => PathMarkup.TryParse(s, null, out _) ? Geometry.Parse(s) : null,
			_ when target == typeof(PointCollection) => Points(s),
			_ when target == typeof(DoubleCollection) => Doubles(s),
			_ when target == typeof(Matrix3x2) => XamlValueSyntax.ParseNumbers(s) is [var m11, var m12, var m21, var m22, var dx, var dy]
				? new Matrix3x2(m11, m12, m21, m22, dx, dy)
				: null,
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

	/// <summary>
	/// Convert attribute text to a value of <paramref name="property"/> as the XAML generator does for that property:
	/// what <see cref="TryParseValue(Type, string, out object?)"/> reads for its value type, and <c>Auto</c> (any case)
	/// for <see cref="FrameworkElement.Width"/> and <see cref="FrameworkElement.Height"/>, read as NaN, sized to content,
	/// as WinUI's length converter reads it.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static bool TryParseValue(DependencyProperty property, string text, out object? value)
	{
		ArgumentNullException.ThrowIfNull(property);
		ArgumentNullException.ThrowIfNull(text);
		RequireEnabled();
		if (XamlValueSyntax.IsAutoLength(property.OwnerType.FullName ?? "", property.Name, text))
		{
			value = float.NaN;
			return true;
		}
		return TryParseValue(property.ValueType, text, out value);
	}

	/// <summary>Whether <see cref="TryParseValue(Type, string, out object?)"/> has a syntax for <paramref name="type"/>, so a value of it can be set
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
		|| target == typeof(float) || target == typeof(double) || target == typeof(HdrColor) || target == typeof(Brush)
		|| target == typeof(SolidColorBrush) || target == typeof(Thickness)
		|| target == typeof(Point) || target == typeof(Vector2) || target == typeof(Vector3) || target == typeof(GridLength)
		|| target == typeof(TimeSpan) || target == typeof(RepeatBehavior) || target == typeof(CornerRadius) || target == typeof(Size)
		|| target == typeof(Rect) || target == typeof(Matrix3x2) || target == typeof(Geometry)
		|| target == typeof(PointCollection) || target == typeof(DoubleCollection);

	// Pairs of numbers, "x1,y1 x2,y2 …", as the generator reads a point list; empty text is an empty list, as it is there.
	private static PointCollection? Points(string s)
	{
		if (s.Length == 0)
			return [];
		if (XamlValueSyntax.ParseNumbers(s) is not { } numbers || numbers.Length % 2 != 0)
			return null;
		var points = new PointCollection();
		for (var i = 0; i < numbers.Length; i += 2)
			points.Add(new Point(numbers[i], numbers[i + 1]));
		return points;
	}

	// Numbers separated by commas or spaces, as the generator reads a dash array; empty text is an empty list, as it is
	// there. The generator writes each number it read as a double literal of its round-trip text, so each is read back
	// from that text here: the double a widened float would give differs (0.1 reads as 0.1, not 0.10000000149011612).
	private static DoubleCollection? Doubles(string s)
	{
		if (s.Length == 0)
			return [];
		if (XamlValueSyntax.ParseNumbers(s) is not { } numbers)
			return null;
		var doubles = new DoubleCollection();
		foreach (var n in numbers)
			doubles.Add(double.Parse(n.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
		return doubles;
	}
}
