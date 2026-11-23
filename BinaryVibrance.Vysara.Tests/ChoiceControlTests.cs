using System.Numerics;

using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Extensions;
using BinaryVibrance.Vysara.Input;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The controls that take a choice or a value: <see cref="ToggleSwitch"/>, <see cref="RadioButton"/> and
/// <see cref="RadioButtons"/>, <see cref="ComboBox"/>, <see cref="NumberBox"/>, <see cref="Expander"/>,
/// <see cref="InfoBar"/>, and <see cref="GridSplitter"/> resizing a grid's tracks.
/// </summary>
public sealed class ChoiceControlTests
{
	private static readonly Vector2 Surface = new(500f, 400f);

	private static UIHost Hosted(UIElement content)
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = content;
		host.Tick(0.016, 0f, Surface);
		return host;
	}

	private static void Key(UIHost host, VirtualKey key, ModifierKeys modifiers = ModifierKeys.None)
	{
		host.QueueKey(new KeyInput(key, Down: true, Repeat: false, modifiers, 0));
		host.QueueKey(new KeyInput(key, Down: false, Repeat: false, modifiers, 0));
		host.Tick(0.016, 0f, Surface);
	}

	private static void Click(UIHost host, UIElement element)
	{
		var at = element.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, element.LayoutRect.Width, element.LayoutRect.Height));
		var point = new Vector2(at.X + at.Width / 2f, at.Y + at.Height / 2f);
		host.QueuePointer(PointerInput.Press(point));
		host.QueuePointer(PointerInput.Release(point));
		host.Tick(0.016, 0f, Surface);
	}

	private static StackPanel Top(params UIElement[] children)
	{
		var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		foreach (var child in children)
			panel.Children.Add(child);
		return panel;
	}

	[Test]
	public void A_toggle_switch_flips_on_a_click_and_a_key()
	{
		var toggle = new ToggleSwitch();
		var host = Hosted(Top(toggle));
		var changes = 0;
		toggle.IsOnChanged += (_, _) => changes++;

		Click(host, toggle);
		toggle.IsOn.ShouldBeTrue();
		toggle.Focus(FocusState.Keyboard);
		Key(host, VirtualKey.Space);
		toggle.IsOn.ShouldBeFalse();
		changes.ShouldBe(2);
	}

	[Test]
	public void Radio_buttons_sharing_a_parent_are_one_choice()
	{
		var a = new RadioButton { Content = "a" };
		var b = new RadioButton { Content = "b" };
		Hosted(Top(a, b));

		a.PerformClick();
		b.PerformClick();
		(a.IsChecked, b.IsChecked).ShouldBe((false, true));
		b.PerformClick();
		b.IsChecked.ShouldBe(true);   // a click never unchecks
	}

	[Test]
	public void Radio_buttons_choose_by_property_click_and_arrow()
	{
		var choices = new RadioButtons { ItemsSource = new[] { "auto", "on", "off" } };
		var host = Hosted(Top(choices));

		choices.SelectedItem = "on";
		choices.SelectedIndex.ShouldBe(1);

		var buttons = Children<RadioButton>(choices);
		buttons[2].PerformClick();
		choices.SelectedItem.ShouldBe("off");

		buttons[2].Focus(FocusState.Keyboard);
		Key(host, VirtualKey.Up);
		choices.SelectedIndex.ShouldBe(1);
	}

	[Test]
	public void A_combo_box_opens_chooses_and_steps()
	{
		var combo = new ComboBox { ItemsSource = new[] { "rest", "build", "drop" }, PlaceholderText = "Pick one" };
		var host = Hosted(Top(combo));

		Click(host, combo);
		combo.IsDropDownOpen.ShouldBeTrue();
		Key(host, VirtualKey.Down);   // in the list, from the first item
		Key(host, VirtualKey.Enter);
		combo.IsDropDownOpen.ShouldBeFalse();
		combo.SelectedItem.ShouldBe("build");

		Key(host, VirtualKey.Down);   // closed, focus back on the box: steps the choice
		combo.SelectedIndex.ShouldBe(2);
	}

	[Test]
	public void A_combo_box_keeps_its_choice_while_it_is_still_offered()
	{
		var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedItem = "b" };
		Hosted(Top(combo));
		combo.SelectedIndex.ShouldBe(1);

		combo.ItemsSource = new[] { "b", "c" };
		combo.SelectedIndex.ShouldBe(0);
		combo.ItemsSource = new[] { "c" };
		combo.SelectedIndex.ShouldBe(-1);
	}

	[Test]
	public void A_number_box_takes_typed_and_stepped_values_inside_its_range()
	{
		var number = new NumberBox { Minimum = 0, Maximum = 1, SmallChange = 0.05, Value = 0.5 };
		var host = Hosted(Top(number));
		var text = Children<TextBox>(number)[0];

		text.Focus(FocusState.Keyboard);
		Key(host, VirtualKey.Up);
		number.Value.ShouldBe(0.55, 1e-9);

		text.Text = "7";
		Key(host, VirtualKey.Enter);
		number.Value.ShouldBe(1d);

		text.Text = "not a number";
		Key(host, VirtualKey.Enter);
		number.Value.ShouldBe(1d);
		text.Text.ShouldBe(1d.ToString("F2", System.Globalization.CultureInfo.CurrentCulture));
	}

	[Test]
	public void An_expander_shows_its_content_while_open()
	{
		var content = new Border { Height = 40f };
		var expander = new Expander { Header = "Exits", Content = content };
		var host = Hosted(Top(expander));
		content.Visibility.ShouldBe(Visibility.Visible);   // the content itself; its panel is what collapses
		content.LayoutRect.Height.ShouldBe(0f);

		Click(host, Children<ContentPresenter>(expander)[0]);
		expander.IsExpanded.ShouldBeTrue();
		host.Tick(0.016, 0f, Surface);
		content.LayoutRect.Height.ShouldBe(40f);
	}

	[Test]
	public void A_press_on_an_expanders_content_leaves_it_open()
	{
		var content = new Border { Height = 40f, Width = 120f, Background = Media.Brush.Sealed(HdrColor.FromRgb(10, 10, 10)) };
		var expander = new Expander { Header = "Chords", Content = content, IsExpanded = true };
		var host = Hosted(Top(expander));

		Click(host, content);
		expander.IsExpanded.ShouldBeTrue();

		Click(host, Children<ContentPresenter>(expander)[0]);
		expander.IsExpanded.ShouldBeFalse();
	}

	[Test]
	public void An_info_bar_closes_from_its_button()
	{
		var bar = new InfoBar { IsOpen = true, Title = "Build failed", Message = "Songs.cs(4): expected ;", Severity = InfoBarSeverity.Error };
		Hosted(Top(bar));
		var closed = 0;
		bar.Closed += (_, _) => closed++;

		Children<Button>(bar)[0].PerformClick();
		bar.IsOpen.ShouldBeFalse();
		closed.ShouldBe(1);
	}

	[Test]
	public void A_grid_splitter_moves_the_line_between_two_rows()
	{
		var grid = new Grid { Height = 300f };
		grid.RowDefinitions.Add(GridLength.Star);
		grid.RowDefinitions.Add(GridLength.Pixels(6f));
		grid.RowDefinitions.Add(GridLength.Pixels(100f));
		var splitter = new GridSplitter();
		Grid.SetRow(splitter, 1);
		grid.Children.Add(splitter);
		var host = Hosted(grid);
		var middle = splitter.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, splitter.LayoutRect.Width, splitter.LayoutRect.Height));
		var start = new Vector2(middle.X + 10f, middle.Y + 3f);

		host.QueuePointer(PointerInput.Press(start));
		host.Tick(0.016, 0f, Surface);
		host.QueuePointer(PointerInput.Move(start - new Vector2(0f, 50f), PointerButtons.Left));
		host.Tick(0.016, 0f, Surface);
		host.QueuePointer(PointerInput.Release(start - new Vector2(0f, 50f)));
		host.Tick(0.016, 0f, Surface);

		grid.RowDefinitions[0].Value.ShouldBe(144f, 0.5f);
		grid.RowDefinitions[2].Value.ShouldBe(150f, 0.5f);
	}

	[Test]
	public void A_scroll_viewer_tells_of_its_view_moving_by_the_wheel_or_from_code()
	{
		var viewer = new ScrollViewer { Height = 100f, Width = 200f, Content = new Border { Height = 400f, Background = new Media.SolidColorBrush(HdrColor.FromRgb(1, 1, 1)) } };
		var host = Hosted(Top(viewer));
		var moves = 0;
		viewer.ViewChanged += (_, e) => moves += e.IsIntermediate ? 0 : 1;

		host.QueuePointer(PointerInput.Scroll(new Vector2(50f, 50f), new Vector2(0f, -1f)));
		host.Tick(0.016, 0f, Surface);
		viewer.ScrollToVerticalOffset(200f);
		viewer.ScrollToVerticalOffset(200f);   // already there: no change, no event

		moves.ShouldBe(2);
		viewer.VerticalOffset.ShouldBe(200f);
	}

	private static List<T> Children<T>(UIElement element) where T : UIElement
	{
		var found = new List<T>();
		void Walk(UIElement e)
		{
			if (e is T t && !ReferenceEquals(e, element))
				found.Add(t);
			foreach (var child in e.VisualChildren)
				Walk(child);
		}
		Walk(element);
		return found;
	}
}
