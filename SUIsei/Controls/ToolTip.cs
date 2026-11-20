using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The small panel <see cref="ToolTipService.ToolTipProperty"/> shows over an element the pointer rests on: its
/// <see cref="ContentControl.Content"/>, text or an element, on a rounded panel no wider than 320 DIPs unless its MaxWidth is set.
/// It never takes the pointer, so what is under it stays hovered. A page recolours it with the <c>ToolTipBackground</c>,
/// <c>ToolTipBorderBrush</c> and <c>ToolTipForeground</c> resources, as WinUI's lightweight styling does.
/// </summary>
public sealed class ToolTip : ContentControl
{
	/// <summary>How wide a tooltip grows before its text wraps, unless its MaxWidth is set.</summary>
	internal const float MaxWidthDefault = 320f;

	// The built-in look's brushes, shared by every default tooltip and never changed.
	private static readonly SolidColorBrush PanelBackground = Brush.Sealed(HdrColor.FromRgb(43, 45, 54));
	private static readonly SolidColorBrush PanelBorder = Brush.Sealed(HdrColor.FromRgb(78, 82, 96));
	private static readonly Thickness BuiltInPadding = new(8f, 5f, 8f, 6f);

	public ToolTip()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		SetBuiltInValue(PaddingProperty, BuiltInPadding);
		SetBuiltInValue(MaxWidthProperty, MaxWidthDefault);
		SetBuiltInValue(FontSizeProperty, 12f);
		SetBuiltInValue(IsHitTestVisibleProperty, false);
		InheritedText.GiveBuiltInLook(this);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border { CornerRadius = new CornerRadius(4f), BorderThickness = new Thickness(1f) };
			host.RegisterTemplateBinding(PaddingProperty, border, Border.PaddingProperty);
			var presenter = new ContentPresenter();
			border.Child = presenter;
			var background = host.ThemeResource("ToolTipBackground", PanelBackground);
			var edge = host.ThemeResource("ToolTipBorderBrush", PanelBorder);
			TemplateStates.ThemedForeground(host, "ToolTipForeground");
			host.RegisterTemplateBinding(BackgroundProperty, border, Border.BackgroundProperty, value => value ?? background);
			host.RegisterTemplateBinding(BorderBrushProperty, border, Border.BorderBrushProperty, value => value ?? edge);
			host.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			host.RegisterContentTemplateBinding(content => presenter.Content = content);
			return border;
		},
	};
}
