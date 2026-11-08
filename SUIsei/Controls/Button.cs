using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ButtonBase"/> with a default look: a rounded <see cref="Border"/> hosting a
/// <see cref="ContentPresenter"/> (<c>{TemplateBinding Content}</c>). As WinUI's default template, the border paints the
/// button's <see cref="Control.Background"/>, <see cref="Control.BorderBrush"/> and <see cref="Control.BorderThickness"/>,
/// and the presenter its <see cref="Control.Foreground"/>; where those are unset it shows the built-in look. A
/// <c>CommonStates</c> <see cref="VisualStateManager"/> group shows the pointer-over and pressed backgrounds, faded in
/// and out by the border's <see cref="BrushTransition"/>. Click synthesis, <see cref="ButtonBase.Click"/>, and
/// <see cref="ButtonBase.Command"/> all come from the base.
/// </summary>
public sealed class Button : ButtonBase
{
	// The built-in look's brushes, shared by every default button and never changed.
	private static readonly SolidColorBrush NormalBackground = Brush.Sealed(HdrColor.FromRgb(60, 60, 70));
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));
	private static readonly SolidColorBrush PressedBackground = Brush.Sealed(HdrColor.FromRgb(40, 40, 55));

	public Button() => SetBuiltInValue(TemplateProperty, DefaultTemplate);

	// The built-in look, shared (stateless) across default buttons: a rounded Border hosting a ContentPresenter, with a
	// CommonStates group whose states show their own background on the border. The storyboards target the border created
	// here, so they (and the VSM groups) are built in the factory rather than the constructor.
	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border
			{
				CornerRadius = new CornerRadius(4f),
				Padding = new Thickness(10f, 5f, 10f, 5f),
				BackgroundTransition = TemplateStates.Fade(120),
			};
			var presenter = new ContentPresenter();
			border.Child = presenter;
			host.RegisterTemplateBinding(BackgroundProperty, value => border.Background = value ?? NormalBackground);
			host.RegisterTemplateBinding(BorderBrushProperty, value => border.BorderBrush = value);
			host.RegisterTemplateBinding(BorderThicknessProperty, value => border.BorderThickness = value);
			host.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			presenter.Content = host.Content;                        // {TemplateBinding Content} — initial
			host.ContentChanged += (_, _) => presenter.Content = host.Content; // live

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = TemplateStates.RestoreBackground(border) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(border, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(border, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};
}
