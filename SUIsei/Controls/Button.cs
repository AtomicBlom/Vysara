using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ButtonBase"/> with a default look: a rounded <see cref="Border"/> hosting a
/// <see cref="ContentPresenter"/> (<c>{TemplateBinding Content}</c>). As WinUI's default template, the border paints the
/// button's <see cref="Control.Background"/>, <see cref="Control.BorderBrush"/> and <see cref="Control.BorderThickness"/>,
/// and the presenter its <see cref="Control.Foreground"/>; where those are unset it shows the built-in look. A
/// <c>CommonStates</c> <see cref="VisualStateManager"/> group shows the pointer-over and pressed backgrounds, faded in
/// and out by the border's <see cref="BrushTransition"/>, and while disabled the disabled background, border and text.
/// Click synthesis, <see cref="ButtonBase.Click"/>, and <see cref="ButtonBase.Command"/> all come from the base.
/// </summary>
public sealed class Button : ButtonBase
{
	// The built-in look's brushes, shared by every default button and never changed.
	private static readonly SolidColorBrush NormalBackground = Brush.Sealed(HdrColor.FromRgb(60, 60, 70));
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));
	private static readonly SolidColorBrush PressedBackground = Brush.Sealed(HdrColor.FromRgb(40, 40, 55));
	// The built-in look's padding, which Padding replaces where it is set.
	internal static readonly Thickness BuiltInPadding = new(10f, 5f, 10f, 5f);
	public static readonly DependencyProperty<Button, Primitives.FlyoutBase?> FlyoutProperty =
		DependencyProperty.Register(nameof(Flyout), new PropertyMetadata<Button, Primitives.FlyoutBase?>());

	/// <summary>A flyout the button opens against itself when clicked, as WinUI's <c>Button.Flyout</c>.</summary>
	public Primitives.FlyoutBase? Flyout { get => GetValue(FlyoutProperty); set => SetValue(FlyoutProperty, value); }

	protected override void OnClick()
	{
		base.OnClick();
		Flyout?.ShowAt(this);
	}

	public Button()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		SetBuiltInValue(PaddingProperty, BuiltInPadding);
		InheritedText.GiveBuiltInLook(this);
	}

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
				BackgroundTransition = TemplateStates.Fade(120),
			};
			host.RegisterTemplateBinding(PaddingProperty, border, Border.PaddingProperty);
			var presenter = new ContentPresenter();
			border.Child = presenter;
			// As WinUI's, a page recolours the built-in look with ButtonBackground and the rest where its resources give them.
			var background = host.ThemeResource("ButtonBackground", NormalBackground);
			var over = host.ThemeResource("ButtonBackgroundPointerOver", OverBackground);
			var pressed = host.ThemeResource("ButtonBackgroundPressed", PressedBackground);
			TemplateStates.ThemedForeground(host, "ButtonForeground");
			host.RegisterTemplateBinding(BackgroundProperty, border, Border.BackgroundProperty, value => value ?? background);
			host.RegisterTemplateBinding(BorderBrushProperty, border, Border.BorderBrushProperty);
			host.RegisterTemplateBinding(BorderThicknessProperty, border, Border.BorderThicknessProperty);
			host.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			host.RegisterContentTemplateBinding(content => presenter.Content = content); // {TemplateBinding Content}

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(border, over) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(border, pressed) });
			common.States.Add(new VisualState { Name = "Disabled", Storyboard = TemplateStates.Disabled(border, TemplateStates.DisabledBackground, presenter) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};
}
