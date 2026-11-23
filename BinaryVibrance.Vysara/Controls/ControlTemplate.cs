using BinaryVibrance.Vysara.Animation;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// Defines a control's visual tree. The <see cref="Factory"/> — generated from a
/// <c>&lt;ControlTemplate&gt;</c>, or hand-written — <c>new</c>s the subtree given the templated control, wiring its
/// <c>{TemplateBinding}</c>s and <see cref="VisualStateManager"/> groups at instantiation.
/// </summary>
public sealed class ControlTemplate : FrameworkTemplate
{
	/// <summary>Builds the template's root element for the given templated control.</summary>
	public required Func<Control, FrameworkElement> Factory { get; init; }
}
