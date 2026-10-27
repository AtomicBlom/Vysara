using System.Globalization;
using System.Numerics;
using System.Text;

using RoseMcp.Tap.Protocol;

using SUIsei.Animation;

namespace SUIsei.RoseTap;

/// <summary>
/// The reply to <c>properties</c>: <c>ok</c>, an element row with the element's type and where it was written, then a
/// row per dependency property with its value and where the value came from. Without <c>all</c>, properties at their
/// default are left out.
/// <para>
/// Values are written in the XAML syntax the property would be set with, in the invariant culture: a colour as
/// <c>#AARRGGBB</c>, a thickness or a point as its numbers, a grid length as <c>Auto</c>, <c>N*</c> or a number. A
/// property list is read by somebody about to edit it, and a value read back in the form it is written in is one they
/// can compare with their markup.
/// </para>
/// </summary>
internal static class TapProperties
{
	/// <summary>The rows for <paramref name="element"/>. Called on the UI thread.</summary>
	public static string Rows(UIElement element, bool includeDefaults)
	{
		var rows = new StringBuilder(TapRows.PropertiesOk).Append('\n');
		var source = Diagnostics.SourceOf(element);
		rows.Append(TapRows.Element(TapTree.TypeName(element.GetType()), source?.File ?? "", source?.Line ?? 0, source?.Column ?? 0)).Append('\n');

		foreach (var property in Diagnostics.Properties(element))
		{
			if (!includeDefaults && property.Level == Diagnostics.ValueLevel.Default)
				continue;
			var (value, unavailable) = Format(property.Value);
			rows.Append(TapRows.Property(
				NameOf(property),
				value,
				TapTree.TypeName(property.Value?.GetType() ?? Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType),
				TapTree.TypeName(property.OwnerType),
				Provenance(property.Level),
				property.Source?.File ?? "",
				property.Source?.Line ?? 0,
				property.Source?.Column ?? 0,
				unavailable,
				Writable(property.ValueType))).Append('\n');
		}
		return rows.ToString();
	}

	/// <summary>The name the XAML taps give a level: a template's value is <c>ParentTemplate</c>.</summary>
	public static string Provenance(Diagnostics.ValueLevel level) => level switch
	{
		Diagnostics.ValueLevel.Animation => "Animation",
		Diagnostics.ValueLevel.Local => "Local",
		Diagnostics.ValueLevel.Template => "ParentTemplate",
		Diagnostics.ValueLevel.Style => "Style",
		Diagnostics.ValueLevel.BuiltInStyle => "BuiltInStyle",
		Diagnostics.ValueLevel.Inherited => "Inherited",
		_ => "Default",
	};

	/// <summary>Whether a property of <paramref name="valueType"/> can be set live: <see cref="TapRows.Writable"/>, or why
	/// it cannot.</summary>
	public static string Writable(Type valueType) => Diagnostics.CanParseValue(valueType)
		? TapRows.Writable
		: $"not settable live: SUIsei has no markup syntax for a {FriendlyName(valueType)}";

	/// <summary>A type as a sentence names it: its short name, with a <c>?</c> for a nullable value type.</summary>
	public static string FriendlyName(Type type) =>
		Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

	/// <summary>
	/// A value as markup writes it, or null for null. The second item is true when the value could not be written at
	/// all, which the row reports rather than letting one odd value fail the whole list.
	/// </summary>
	public static (string? Text, bool Unavailable) Format(object? value)
	{
		try
		{
			return (value switch
			{
				null => null,
				string text => text,
				bool flag => flag ? "True" : "False",
				HdrColor color => Color(color),
				// A solid brush as the colour it paints, which is how markup writes one and what parses back to it.
				SUIsei.Media.SolidColorBrush brush => Color(brush.Color),
				Thickness t => Numbers(t.Left, t.Top, t.Right, t.Bottom),
				Point p => Numbers(p.X, p.Y),
				Vector2 v => Numbers(v.X, v.Y),
				Vector3 v => Numbers(v.X, v.Y, v.Z),
				GridLength length => length.Type switch
				{
					GridUnitType.Auto => "Auto",
					GridUnitType.Star => length.Value == 1f ? "*" : Number(length.Value) + "*",
					_ => Number(length.Value),
				},
				RepeatBehavior repeat => repeat.IsForever ? "Forever" : repeat.Count.ToString(CultureInfo.InvariantCulture) + "x",
				IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
				_ => Invariant(value),
			}, false);
		}
		catch (Exception)
		{
			return ("", true);
		}
	}

	// An attached property is named with its owner, as markup sets it: Grid.Row.
	private static string NameOf(Diagnostics.PropertyValue property) =>
		property.Property.Kind == DependencyPropertyKind.Attached ? property.OwnerType.Name + "." + property.Name : property.Name;

	// A colour in the authoring range as #AARRGGBB, which parses back to the same value. One with a channel above paper
	// white has no such form and is written as its channels.
	private static string Color(HdrColor color)
	{
		var inRange = InUnitRange(color.R) && InUnitRange(color.G) && InUnitRange(color.B) && InUnitRange(color.A);
		if (!inRange)
			return Invariant(color);
		return string.Create(CultureInfo.InvariantCulture, $"#{Byte(color.A):X2}{Byte(color.R):X2}{Byte(color.G):X2}{Byte(color.B):X2}");
	}

	private static bool InUnitRange(float channel) => channel is >= 0f and <= 1f;

	private static byte Byte(float channel) => (byte)MathF.Round(channel * 255f);

	private static string Numbers(params float[] values) => string.Join(",", values.Select(Number));

	private static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

	// A type with no syntax of its own, written by its ToString with the invariant culture current, since a record's
	// ToString formats its members with whatever culture the thread has.
	private static string Invariant(object value)
	{
		var culture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			return value.ToString() ?? "";
		}
		finally
		{
			CultureInfo.CurrentCulture = culture;
		}
	}
}
