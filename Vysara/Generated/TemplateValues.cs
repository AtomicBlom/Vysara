using System.ComponentModel;

namespace Vysara.Generated;

/// <summary>
/// What a generated <c>ControlTemplate</c> factory calls to set a value on a part of the template, as WinUI sets every
/// value a template gives its parts: at the templated-parent level, below the part's local value. So a value the app sets
/// on a part wins over the template's, <c>ClearValue</c> on a part goes back to the template's value rather than dropping
/// it, and a diagnostics tool reports the value as the template's. In a namespace markup doesn't map, since it is for
/// generated code only.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TemplateValues
{
	/// <summary>Set <paramref name="property"/> on <paramref name="part"/> to <paramref name="value"/> as its template's.</summary>
	public static void Set<TOwner, TValue>(DependencyObject part, DependencyProperty<TOwner, TValue> property, TValue value)
		where TOwner : DependencyObject
	{
		ArgumentNullException.ThrowIfNull(part);
		part.SetTemplateValue(property, value);
	}
}
