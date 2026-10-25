using System.Numerics;

namespace SUIsei.Controls;

/// <summary>The base of multi-child layout containers: owns the <see cref="Children"/>
/// collection and an optional solid <see cref="Background"/>. Subclasses implement the measure/arrange policy.</summary>
[ContentProperty(Name = nameof(Children))]
public abstract class Panel : FrameworkElement
{
	protected Panel() => Children = new UIElementCollection(this);

	public UIElementCollection Children { get; }

	/// <summary>An optional solid fill behind the children (hit-testing makes a backgroundless panel transparent).</summary>
	public HdrColor? Background { get; set { if (field != value) { field = value; InvalidateContent(); } } }

	internal override IReadOnlyList<UIElement> VisualChildren => Children.Items;

	private protected override VisualContent? UpdateContent() => Background is { } bg ? RetainedRect(bg) : null;
}
