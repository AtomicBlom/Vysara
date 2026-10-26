using SUIsei.Controls;

namespace SUIsei.Extensions;

/// <summary>
/// Settings for SUIsei's <see cref="VirtualizingStackPanel"/>, set in XAML as
/// <c>&lt;VirtualizingStackPanel suisei:Virtualization.RowHeight="32" /&gt;</c> inside an <c>ItemsPanelTemplate</c>.
/// WinUI's virtualizing panels measure their items; SUIsei's assumes one uniform row height, so the visible window is
/// arithmetic from the scroll offset.
/// </summary>
public sealed class Virtualization : DependencyObject
{
	private Virtualization()
	{
	}

	/// <summary>The uniform height of each item, in DIPs. 24 by default.</summary>
	public static readonly DependencyProperty<Virtualization, float> RowHeightProperty =
		DependencyProperty.RegisterAttached<Virtualization, float>("RowHeight",
			new PropertyMetadata<Virtualization, float> { DefaultValue = 24f, Invalidates = InvalidationFlags.Measure });

	public static void SetRowHeight(VirtualizingStackPanel panel, float value) => panel.SetValue(RowHeightProperty, value);
	public static float GetRowHeight(VirtualizingStackPanel panel) => panel.GetValue(RowHeightProperty);
}
