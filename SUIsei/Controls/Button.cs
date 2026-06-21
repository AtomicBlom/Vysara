namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ButtonBase"/> with a default look (PlanningUI.md §D2.1/§D5.3/§D5.7): a rounded
/// <see cref="Border"/> hosting a <see cref="ContentPresenter"/> (<c>{TemplateBinding Content}</c>) with a
/// <c>CommonStates</c> <see cref="VisualStateManager"/> group that animates the background on hover/press. Click
/// synthesis, <see cref="ButtonBase.Click"/>, and <see cref="ButtonBase.Command"/> all come from the base.
/// </summary>
public sealed class Button : ButtonBase
{
	private static readonly HdrColor NormalBackground = HdrColor.FromRgb(60, 60, 70);
	private static readonly HdrColor OverBackground = HdrColor.FromRgb(80, 80, 95);
	private static readonly HdrColor PressedBackground = HdrColor.FromRgb(40, 40, 55);

	public Button() => Template = DefaultTemplate;

	// The built-in look, shared (stateless) across default buttons: a rounded Border hosting a ContentPresenter,
	// with a CommonStates group animating the border background. The storyboards target the border created here, so
	// they (and the VSM groups) are built in the factory rather than the constructor.
	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border
			{
				Background = NormalBackground,
				CornerRadius = 4f,
				Padding = new Thickness(10f, 5f, 10f, 5f),
			};
			var presenter = new ContentPresenter();
			border.Child = presenter;
			presenter.Content = host.Content;                        // {TemplateBinding Content} — initial
			host.ContentChanged += (_, _) => presenter.Content = host.Content; // live

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = BackgroundTo(border, OverBackground, NormalBackground) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BackgroundTo(border, NormalBackground, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = BackgroundTo(border, OverBackground, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};

	// A short background-colour transition between two states, applied to the template's border on the no-relayout
	// visual path.
	private static Storyboard BackgroundTo(Border target, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Apply = target.SetAnimatedFill, Release = target.ClearAnimatedFill,
			},
		},
	};
}
