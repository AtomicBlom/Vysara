using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Controls.Primitives;

/// <summary>
/// The whole look of a <see cref="ListViewItem"/>, as WinUI's <c>ListViewItemPresenter</c>: the item's built-in template
/// is this one element, which shows the item's content inside <see cref="ContentMargin"/> over a background that follows
/// the item's state. At rest it paints its own <see cref="ContentPresenter.Background"/> (the item's); hovered, pressed or
/// selected it paints the brush its properties give for that state, faded by its
/// <see cref="ContentPresenter.BackgroundTransition"/>, and a selected item's text takes <see cref="SelectedForeground"/>
/// where one is given. A state shows by the presenter swapping which brush it paints, with no storyboard per item.
/// Disabled, a selected item shows <see cref="SelectedDisabledBackground"/> and the text dims to the shared disabled
/// brush, rather than fading the item, which Vysara's built-in looks never do. Not provided: WinUI's check mark and
/// selection indicator, focus visuals, reveal, and <c>DisabledOpacity</c>.
/// </summary>
public sealed class ListViewItemPresenter : ContentPresenter
{
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> PointerOverBackgroundProperty = StateBrush(nameof(PointerOverBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> PressedBackgroundProperty = StateBrush(nameof(PressedBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> SelectedBackgroundProperty = StateBrush(nameof(SelectedBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> SelectedPointerOverBackgroundProperty = StateBrush(nameof(SelectedPointerOverBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> SelectedPressedBackgroundProperty = StateBrush(nameof(SelectedPressedBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> SelectedDisabledBackgroundProperty = StateBrush(nameof(SelectedDisabledBackground));
	public static readonly DependencyProperty<ListViewItemPresenter, Brush?> SelectedForegroundProperty = StateBrush(nameof(SelectedForeground));

	public static readonly DependencyProperty<ListViewItemPresenter, Thickness> ContentMarginProperty =
		DependencyProperty.Register(nameof(ContentMargin),
			new PropertyMetadata<ListViewItemPresenter, Thickness> { PropertyChanged = static (p, _, _) => p.InvalidateMeasure() });

	private ListViewItemState _state;

	/// <summary>The background while the pointer is over an unselected item.</summary>
	public Brush? PointerOverBackground { get => GetValue(PointerOverBackgroundProperty); set => SetValue(PointerOverBackgroundProperty, value); }

	/// <summary>The background while an unselected item is pressed; the pointer-over one where null.</summary>
	public Brush? PressedBackground { get => GetValue(PressedBackgroundProperty); set => SetValue(PressedBackgroundProperty, value); }

	/// <summary>The background of a selected item.</summary>
	public Brush? SelectedBackground { get => GetValue(SelectedBackgroundProperty); set => SetValue(SelectedBackgroundProperty, value); }

	/// <summary>The background while the pointer is over a selected item; the selected one where null.</summary>
	public Brush? SelectedPointerOverBackground
	{
		get => GetValue(SelectedPointerOverBackgroundProperty);
		set => SetValue(SelectedPointerOverBackgroundProperty, value);
	}

	/// <summary>The background while a selected item is pressed; the selected pointer-over one where null.</summary>
	public Brush? SelectedPressedBackground { get => GetValue(SelectedPressedBackgroundProperty); set => SetValue(SelectedPressedBackgroundProperty, value); }

	/// <summary>The background of a selected item while it is disabled.</summary>
	public Brush? SelectedDisabledBackground
	{
		get => GetValue(SelectedDisabledBackgroundProperty);
		set => SetValue(SelectedDisabledBackgroundProperty, value);
	}

	/// <summary>The text of a selected item, or null to leave it the item's.</summary>
	public Brush? SelectedForeground { get => GetValue(SelectedForegroundProperty); set => SetValue(SelectedForegroundProperty, value); }

	/// <summary>The room kept between the item's edges and its content: its <see cref="Control.Padding"/>.</summary>
	public Thickness ContentMargin { get => GetValue(ContentMarginProperty); set => SetValue(ContentMarginProperty, value); }

	private static DependencyProperty<ListViewItemPresenter, Brush?> StateBrush(string name) =>
		DependencyProperty.Register(name, new PropertyMetadata<ListViewItemPresenter, Brush?> { PropertyChanged = static (p, _, _) => p.Show(p._state, useTransitions: false) });

	/// <summary>Shows <paramref name="state"/>: the brushes painted above the presenter's own background and text, on the
	/// animation rung a state's storyboard would hold them on, so they outrank the item's values and let them show again
	/// as they are released.</summary>
	internal void Show(ListViewItemState state, bool useTransitions)
	{
		_state = state;
		var background = !state.Enabled
			? state.Selected ? SelectedDisabledBackground : null
			: state.Selected
				? state.Pressed ? SelectedPressedBackground ?? SelectedPointerOverBackground ?? SelectedBackground
				: state.Over ? SelectedPointerOverBackground ?? SelectedBackground
				: SelectedBackground
			: state.Pressed ? PressedBackground ?? PointerOverBackground
			: state.Over ? PointerOverBackground
			: null;
		var foreground = !state.Enabled ? TemplateStates.DisabledForeground : state.Selected ? SelectedForeground : null;

		var transition = BackgroundTransition;
		if (!useTransitions)
			BackgroundTransition = null;
		try
		{
			if (background is null)
				ClearAnimatedValue(BackgroundProperty);
			else
				SetAnimatedValue(BackgroundProperty, background);
		}
		finally
		{
			BackgroundTransition = transition;
		}
		if (foreground is null)
			ClearAnimatedValue(ForegroundProperty);
		else
			SetAnimatedValue(ForegroundProperty, foreground);
	}

	protected override Size MeasureOverride(Size available)
	{
		var margin = ContentMargin;
		var inner = new Size(MathF.Max(0f, available.Width - margin.Horizontal), MathF.Max(0f, available.Height - margin.Vertical));
		var content = base.MeasureOverride(inner);
		return new Size(content.Width + margin.Horizontal, content.Height + margin.Vertical);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var margin = ContentMargin;
		if (VisualChildren is [var content])
		{
			content.Arrange(new Rect(margin.Left, margin.Top,
				MathF.Max(0f, finalSize.Width - margin.Horizontal), MathF.Max(0f, finalSize.Height - margin.Vertical)));
		}
		return finalSize;
	}
}

/// <summary>What a list item shows: whether it is selected, hovered, pressed and enabled.</summary>
internal readonly record struct ListViewItemState(bool Selected, bool Over, bool Pressed, bool Enabled);
