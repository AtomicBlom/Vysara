namespace SUIsei;

public static partial class Diagnostics
{
	/// <summary>Where a dependency property's value came from: the levels of the precedence ladder, lowest first, so a
	/// greater level overrides a lesser one.</summary>
	public enum ValueLevel
	{
		/// <summary>No level holds a value; the property reads its registered default.</summary>
		Default,

		/// <summary>Inherited from an ancestor.</summary>
		Inherited,

		/// <summary>Given by the control itself, as WinUI's default style gives it: a control's built-in template, and
		/// that a control is a tab stop.</summary>
		BuiltInStyle,

		/// <summary>Set by a setter of the element's style.</summary>
		Style,

		/// <summary>Pushed by a <c>{TemplateBinding}</c> onto a template part: what the XAML taps report as
		/// <c>ParentTemplate</c>.</summary>
		Template,

		/// <summary>Set on the element itself, by markup, a binding, code or <see cref="SetLocal"/>.</summary>
		Local,

		/// <summary>Driven by a running animation.</summary>
		Animation,
	}

	/// <summary>One dependency property of an element as <see cref="Properties"/> reads it.</summary>
	/// <param name="Property">The property, to pass to <see cref="SetLocal"/> or <see cref="ClearLocal"/>.</param>
	/// <param name="Name">The property's name, without its owner: <c>Opacity</c>, <c>Row</c>.</param>
	/// <param name="OwnerType">The type that registered it: the declaring control, or for an attached property the
	/// type that declares it (<c>Grid</c> for <c>Grid.Row</c>).</param>
	/// <param name="ValueType">The type of its value, what <see cref="TryParseValue"/> converts text to for it.</param>
	/// <param name="Value">Its effective value, coerced.</param>
	/// <param name="Level">The level <paramref name="Value"/> came from.</param>
	/// <param name="Source">Where in markup the value was set, when that is known: the style setter for a
	/// <see cref="ValueLevel.Style"/> value.</param>
	public sealed record PropertyValue(
		DependencyProperty Property,
		string Name,
		Type OwnerType,
		Type ValueType,
		object? Value,
		ValueLevel Level,
		SourcePosition? Source);

	/// <summary>
	/// Each dependency property of <paramref name="element"/>: every one its type and base types register, including
	/// those at their default, then each attached property it holds a value for. Base types' properties come first.
	/// Reading only looks, so a second call reports what the first did.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static IReadOnlyList<PropertyValue> Properties(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();

		var chain = new List<Type>();
		for (var type = element.GetType(); type is not null && type != typeof(object); type = type.BaseType)
			chain.Add(type);

		var result = new List<PropertyValue>();
		for (var i = chain.Count - 1; i >= 0; i--)
		{
			foreach (var dp in DependencyProperty.RegisteredBy(chain[i]))
			{
				if (dp.Kind == DependencyPropertyKind.Normal)
					result.Add(Describe(element, dp));
			}
		}
		foreach (var index in element.StoredPropertyIndices())
		{
			if (DependencyProperty.FromIndex(index) is { Kind: DependencyPropertyKind.Attached } attached)
				result.Add(Describe(element, attached));
		}
		return result;
	}

	/// <summary>
	/// Set <paramref name="property"/>'s Local level on <paramref name="element"/>, as the app's own markup or code
	/// would. An animation running on it still wins until it ends, and a template's or style's value is overridden.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="value"/> is not of the property's value type, or the property
	/// is not one <paramref name="element"/>'s type has.</exception>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static void SetLocal(DependencyObject element, DependencyProperty property, object? value)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(property);
		RequireEnabled();
		RequireApplies(element, property);
		if (!property.Accepts(value))
		{
			throw new ArgumentException(
				$"{property.Name} takes a {property.ValueType.Name}; {(value is null ? "null" : $"a {value.GetType().Name}")} is not one.",
				nameof(value));
		}
		property.SetRungBoxed(element, DependencyObject.ValueRung.Local, value);
	}

	/// <summary>Clear <paramref name="property"/>'s Local level on <paramref name="element"/>, so the next level down shows
	/// through: a template's value, a style's, an inherited one, or the default.</summary>
	/// <exception cref="ArgumentException">The property is not one <paramref name="element"/>'s type has.</exception>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static void ClearLocal(DependencyObject element, DependencyProperty property)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(property);
		RequireEnabled();
		RequireApplies(element, property);
		property.ClearRung(element, DependencyObject.ValueRung.Local);
	}

	// A normal property belongs to its owner type and those derived from it; an attached one goes on anything.
	private static void RequireApplies(DependencyObject element, DependencyProperty property)
	{
		if (property.Kind == DependencyPropertyKind.Normal && !property.OwnerType.IsInstanceOfType(element))
		{
			throw new ArgumentException(
				$"{property.OwnerType.Name}.{property.Name} is not a property of {element.GetType().Name}.", nameof(property));
		}
	}

	private static PropertyValue Describe(DependencyObject element, DependencyProperty dp)
	{
		var level = element.TopRung(dp) switch
		{
			DependencyObject.ValueRung.Animation => ValueLevel.Animation,
			DependencyObject.ValueRung.Local => ValueLevel.Local,
			DependencyObject.ValueRung.Template => ValueLevel.Template,
			DependencyObject.ValueRung.Style => ValueLevel.Style,
			DependencyObject.ValueRung.BuiltInStyle => ValueLevel.BuiltInStyle,
			DependencyObject.ValueRung.Inherited => ValueLevel.Inherited,
			_ => ValueLevel.Default,
		};
		var source = level == ValueLevel.Style && element is FrameworkElement { Style: { } style } && WinningSetter(style, dp) is { } setter
			? RecordedSource(setter)
			: null;
		return new PropertyValue(dp, dp.Name, dp.OwnerType, dp.ValueType, dp.GetBoxedValue(element), level, source);
	}

	// The setter whose value a style left on the Style level: Style.Apply runs the BasedOn chain first and each style's
	// setters in order, so the last setter for the property in the most derived style that has one.
	private static Setter? WinningSetter(Style style, DependencyProperty dp)
	{
		for (var s = style; s is not null; s = s.BasedOn)
		{
			for (var i = s.Setters.Count - 1; i >= 0; i--)
			{
				if (ReferenceEquals(s.Setters[i].Property, dp))
					return s.Setters[i];
			}
		}
		return null;
	}
}
