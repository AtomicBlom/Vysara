using SUIsei.Controls.Primitives;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A menu, as WinUI's <c>MenuFlyout</c>: its <see cref="Items"/> in a list that scrolls where it doesn't fit, opened below
/// its target with their left edges lined up by default (<see cref="FlyoutBase"/>), or at the pointer as an element's
/// <see cref="UIElement.ContextFlyout"/>. Invoking an item (which fires its <see cref="ButtonBase.Click"/> /
/// <see cref="ButtonBase.Command"/>) or pressing outside closes it.
/// </summary>
public sealed class MenuFlyout : FlyoutBase
{
	// The menu's look. Shared and never changed.
	private static readonly SolidColorBrush MenuBackground = Brush.Sealed(HdrColor.FromRgb(45, 45, 55));
	private static readonly SolidColorBrush MenuBorder = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));

	public MenuFlyout() => Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;

	public List<MenuFlyoutItem> Items { get; } = [];

	// A menu sits against its target, as a menu bar's drops from the bar
	protected override float Gap => 0f;

	/// <summary>Open the menu anchored under <paramref name="target"/> (which must be attached to a live root).</summary>
	public void ShowAt(UIElement target) => ShowAt(target, null);

	protected override FrameworkElement CreatePresenter()
	{
		var panel = new StackPanel { Orientation = Orientation.Vertical };
		foreach (var item in Items)
		{
			item.Click += OnItemClick;
			panel.Children.Add(item);
		}
		return new Border
		{
			Background = Resource(Target!, "MenuFlyoutPresenterBackground", MenuBackground),
			BorderBrush = Resource(Target!, "MenuFlyoutPresenterBorderBrush", MenuBorder),
			BorderThickness = new Thickness(1f),
			CornerRadius = new CornerRadius(4f),
			Padding = new Thickness(4f),
			Child = new ScrollViewer { Content = panel },
		};
	}

	protected override void OnClosed()
	{
		foreach (var item in Items)
			item.Click -= OnItemClick;
	}

	private void OnItemClick(object sender, RoutedEventArgs e) => Hide();
}
