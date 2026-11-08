using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>A single command row in a <see cref="MenuFlyout"/>: a <see cref="ButtonBase"/> with
/// a <see cref="Text"/> label and a flat full-width template that highlights on hover. As in WinUI's default template,
/// the row paints the item's <see cref="Control.Background"/>, <see cref="Control.BorderBrush"/> and
/// <see cref="Control.BorderThickness"/>, and the label its <see cref="Control.Foreground"/>. Click invocation and
/// <see cref="ButtonBase.Command"/> come from the base; the flyout closes itself on invoke.</summary>
public sealed class MenuFlyoutItem : ButtonBase
{
	// The built-in look's brushes, shared by every menu item and never changed.
	private static readonly SolidColorBrush NormalBackground = Brush.Sealed(HdrColor.Transparent);
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(70, 70, 85));
	private static readonly SolidColorBrush PressedBackground = Brush.Sealed(HdrColor.FromRgb(55, 55, 70));

	public MenuFlyoutItem()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch;
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
	}

	/// <summary>The menu row label (maps to <see cref="ContentControl.Content"/>).</summary>
	public string Text
	{
		get => Content as string ?? string.Empty;
		set => Content = value;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border
			{
				CornerRadius = new CornerRadius(2f),
				Padding = new Thickness(12f, 6f),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				BackgroundTransition = TemplateStates.Fade(90),
			};
			var presenter = new ContentPresenter();
			border.Child = presenter;
			host.RegisterTemplateBinding(BackgroundProperty, value => border.Background = value ?? NormalBackground);
			host.RegisterTemplateBinding(BorderBrushProperty, value => border.BorderBrush = value);
			host.RegisterTemplateBinding(BorderThicknessProperty, value => border.BorderThickness = value);
			host.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			presenter.Content = host.Content;
			host.ContentChanged += (_, _) => presenter.Content = host.Content;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = TemplateStates.RestoreBackground(border) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(border, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(border, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};
}
