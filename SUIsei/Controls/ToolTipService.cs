using SUIsei.Controls.Primitives;

namespace SUIsei.Controls;

/// <summary>
/// An element's tooltip, as WinUI's <c>ToolTipService</c>: <c>ToolTipService.ToolTip="Undo (Ctrl Z)"</c> in XAML, or any
/// element or <see cref="ToolTip"/>. Once the pointer has rested on the element (or anything inside it without a tooltip
/// of its own) for <see cref="InitialShowDelay"/>, the host shows it by the element's <see cref="PlacementProperty"/>
/// side; it goes as the pointer leaves, presses, scrolls or a key is pressed, and doesn't come back until the pointer
/// leaves and returns.
/// </summary>
public sealed class ToolTipService : DependencyObject
{
	/// <summary>How long the pointer rests on an element before its tooltip shows, in seconds.</summary>
	public const double InitialShowDelay = 0.6;

	private ToolTipService()
	{
	}

	/// <summary>What the tooltip shows: text, an element, or a <see cref="Controls.ToolTip"/> as it is. Null for none.</summary>
	public static readonly DependencyProperty<ToolTipService, object?> ToolTipProperty =
		DependencyProperty.RegisterAttached<ToolTipService, object?>("ToolTip", new PropertyMetadata<ToolTipService, object?>());

	/// <summary>Which side of the element the tooltip opens on: below by default.</summary>
	public static readonly DependencyProperty<ToolTipService, PlacementMode> PlacementProperty =
		DependencyProperty.RegisterAttached<ToolTipService, PlacementMode>("Placement",
			new PropertyMetadata<ToolTipService, PlacementMode> { DefaultValue = PlacementMode.Bottom });

	public static void SetToolTip(UIElement element, object? value) => element.SetValue(ToolTipProperty, value);
	public static object? GetToolTip(UIElement element) => element.GetValue(ToolTipProperty);

	public static void SetPlacement(UIElement element, PlacementMode value) => element.SetValue(PlacementProperty, value);
	public static PlacementMode GetPlacement(UIElement element) => element.GetValue(PlacementProperty);
}
