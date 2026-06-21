namespace SUIsei.Controls;

/// <summary>A single command row in a <see cref="MenuFlyout"/> (PlanningUI.md MU6): a <see cref="ButtonBase"/> with
/// a <see cref="Text"/> label and a flat full-width template that highlights on hover. Click invocation and
/// <see cref="ButtonBase.Command"/> come from the base; the flyout closes itself on invoke.</summary>
public sealed class MenuFlyoutItem : ButtonBase
{
	private static readonly HdrColor NormalBackground = HdrColor.Transparent;
	private static readonly HdrColor OverBackground = HdrColor.FromRgb(70, 70, 85);
	private static readonly HdrColor PressedBackground = HdrColor.FromRgb(55, 55, 70);

	public MenuFlyoutItem()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch;
		Template = DefaultTemplate;
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
				Background = NormalBackground,
				CornerRadius = 2f,
				Padding = new Thickness(12f, 6f),
				HorizontalAlignment = HorizontalAlignment.Stretch,
			};
			var presenter = new ContentPresenter();
			border.Child = presenter;
			presenter.Content = host.Content;
			host.ContentChanged += (_, _) => presenter.Content = host.Content;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = BackgroundTo(border, OverBackground, NormalBackground) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BackgroundTo(border, NormalBackground, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = BackgroundTo(border, OverBackground, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};

	private static Storyboard BackgroundTo(Border target, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(90), FillBehavior = FillBehavior.HoldEnd,
				Apply = target.SetAnimatedFill, Release = target.ClearAnimatedFill,
			},
		},
	};
}
