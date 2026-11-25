using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Extensions;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The choice and value controls in detail, each as WinUI's: an expander that only its header flips, a number box that
/// never rounds the value it was given and steps only while it has focus, a combo box opened from code and the keyboard
/// and following its source, radio buttons in columns as one tab stop, an info bar's closing events, a toggle switch's
/// disabled look, a grid splitter's columns and lost captures, and a scroll viewer's view changes.
/// </summary>
public sealed class ChoiceControlDetailTests
{
	// --- Expander ---

	/// <summary>Only the header flips an expander: a click on a button in its content, or a key in a text box there, is the
	/// content's own.</summary>
	[Test]
	public void Only_an_expanders_header_flips_it()
	{
		var inner = new Button { Content = "Inside" };
		var text = new TextBox();
		var expander = new Expander { Header = "Exits", IsExpanded = true, Content = UiDriver.Top(inner, text) };
		var ui = new UiDriver(UiDriver.Top(expander));
		var clicks = 0;
		inner.Click += (_, _) => clicks++;

		ui.Click(inner);
		clicks.ShouldBe(1);
		expander.IsExpanded.ShouldBeTrue();

		ui.Click(UiDriver.Parts<Border>(expander)[^1]); // the content's padded panel
		expander.IsExpanded.ShouldBeTrue();

		text.Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.Space);
		ui.Key(VirtualKey.Enter);
		expander.IsExpanded.ShouldBeTrue();

		ui.Click(UiDriver.Parts<ContentPresenter>(expander)[0]); // the header
		expander.IsExpanded.ShouldBeFalse();
		expander.Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.Enter);
		expander.IsExpanded.ShouldBeTrue();
	}

	[Test]
	public void An_expander_says_which_way_it_went_and_opens_up_when_asked()
	{
		var content = new Border { Height = 30f };
		var expander = new Expander { Header = "Exits", Content = content, ExpandDirection = ExpandDirection.Up };
		var ui = new UiDriver(UiDriver.Top(expander));
		var said = new List<string>();
		expander.Expanding += (sender, _) => said.Add($"expanding {sender.Header}");
		expander.Collapsed += (sender, _) => said.Add($"collapsed {sender.Header}");

		expander.IsExpanded = true;
		ui.Tick();
		var header = UiDriver.Parts<ContentPresenter>(expander).First(p => Equals(p.Content, "Exits"));
		UiDriver.Bounds(content).Y.ShouldBeLessThan(UiDriver.Bounds(header).Y); // above its header
		expander.IsExpanded = false;

		said.ShouldBe(["expanding Exits", "collapsed Exits"]);
	}

	[Test]
	public void A_disabled_expander_dims_its_header_and_chevrons()
	{
		var expander = new Expander { Header = "Exits", IsEnabled = false };
		var ui = new UiDriver(UiDriver.Top(expander));
		ui.Tick();

		var header = UiDriver.Parts<Border>(expander)[0];
		header.Background.ShouldBeSameAs(TemplateStates.DisabledBackground);
		UiDriver.Parts<Shapes.Path>(expander).ShouldAllBe(chevron => ReferenceEquals(chevron.Stroke, TemplateStates.DisabledForeground));
	}

	// --- NumberBox ---

	/// <summary>A value set from code is shown in full, and focus passing through the box never rounds it to the step.</summary>
	[Test]
	public void A_number_box_never_rounds_the_value_it_was_given()
	{
		var number = new NumberBox { Value = 0.25 };
		var other = new Button();
		var ui = new UiDriver(UiDriver.Top(number, other));
		var text = UiDriver.Parts<TextBox>(number)[0];
		text.Text.ShouldBe(0.25.ToString(CultureInfo.CurrentCulture));

		text.Focus(FocusState.Keyboard);
		other.Focus(FocusState.Keyboard);
		ui.Tick();
		number.Value.ShouldBe(0.25);
	}

	[Test]
	[Arguments(1d, 3d, "3")]
	[Arguments(0.1, 3d, "3.0")]
	[Arguments(0.05, 1.5, "1.50")]
	[Arguments(1d, 1.234567, "1.234567")]
	public void A_number_box_shows_at_least_its_steps_decimals(double step, double value, string shown) =>
		NumberBox.Format(value, step).ShouldBe(shown.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator));

	[Test]
	public void A_number_box_steps_by_the_wheel_only_while_it_has_focus()
	{
		var number = new NumberBox { Value = 5 };
		var ui = new UiDriver(UiDriver.Top(number));
		var text = UiDriver.Parts<TextBox>(number)[0];

		ui.Wheel(number, new Vector2(0f, 1f));
		number.Value.ShouldBe(5d);

		text.Focus(FocusState.Keyboard);
		ui.Wheel(number, new Vector2(0f, 1f));
		number.Value.ShouldBe(6d);
	}

	[Test]
	public void A_number_box_pages_by_its_large_change_and_holds_NaN_as_no_value()
	{
		var number = new NumberBox { LargeChange = 25, Maximum = 60 };
		var ui = new UiDriver(UiDriver.Top(number));
		var text = UiDriver.Parts<TextBox>(number)[0];
		var seen = new List<(double, double)>();
		number.ValueChanged += (_, e) => seen.Add((e.OldValue, e.NewValue));
		text.Text.ShouldBe("");

		text.Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.PageUp);   // from no value, 0 + 25
		ui.Key(VirtualKey.PageUp);
		ui.Key(VirtualKey.PageUp);   // clamped to 60
		number.Value.ShouldBe(60d);

		text.Text = "";
		ui.Key(VirtualKey.Enter);
		double.IsNaN(number.Value).ShouldBeTrue();
		seen[0].Item1.ShouldBe(double.NaN);
		seen[^1].Item2.ShouldBe(double.NaN);
	}

	[Test]
	public void A_number_box_takes_an_invariant_number_and_shows_a_placeholder_while_empty()
	{
		var number = new NumberBox { PlaceholderText = "Laps" };
		var ui = new UiDriver(UiDriver.Top(number));
		var text = UiDriver.Parts<TextBox>(number)[0];
		var placeholder = UiDriver.Parts<TextBlock>(number).Single(t => t.Text == "Laps");
		placeholder.Visibility.ShouldBe(Visibility.Visible);

		text.Focus(FocusState.Keyboard);
		text.Text = "2.5";
		ui.Key(VirtualKey.Enter);
		number.Value.ShouldBe(2.5);
		placeholder.Visibility.ShouldBe(Visibility.Collapsed);
	}

	// --- ComboBox ---

	[Test]
	[Arguments(VirtualKey.F4, ModifierKeys.None)]
	[Arguments(VirtualKey.Down, ModifierKeys.Alt)]
	public void A_combo_box_opens_from_the_keyboard(VirtualKey key, ModifierKeys modifiers)
	{
		var combo = new ComboBox { ItemsSource = new[] { "a", "b" } };
		var ui = new UiDriver(UiDriver.Top(combo));
		combo.Focus(FocusState.Keyboard);

		ui.Key(key, modifiers);
		combo.IsDropDownOpen.ShouldBeTrue();
		combo.SelectedIndex.ShouldBe(-1); // opened, not stepped
	}

	/// <summary>IsDropDownOpen opens and closes the list from code, as WinUI's, and the list is at least as wide as the box.</summary>
	[Test]
	public void A_combo_box_opens_and_closes_from_code_at_least_as_wide_as_itself()
	{
		var combo = new ComboBox { Width = 220f, ItemsSource = new[] { "a", "b" } };
		var ui = new UiDriver(UiDriver.Top(combo));
		var opened = 0;
		combo.DropDownOpened += (_, _) => opened++;

		combo.IsDropDownOpen = true;
		ui.Tick();
		opened.ShouldBe(1);
		var list = ui.Root.Overlays.SelectMany(o => UiDriver.Parts<ListView>(o.Element)).Single();
		UiDriver.Bounds(list.Parent!).Width.ShouldBeGreaterThanOrEqualTo(220f);

		combo.IsDropDownOpen = false;
		ui.Tick();
		ui.Root.Overlays.SelectMany(o => UiDriver.Parts<ListView>(o.Element)).ShouldBeEmpty();
	}

	[Test]
	public void A_combo_box_follows_its_source_and_raises_one_change_for_an_index_with_no_item()
	{
		var items = new ObservableCollection<string> { "a", "b", "c" };
		var combo = new ComboBox { ItemsSource = items, SelectedItem = "c" };
		var ui = new UiDriver(UiDriver.Top(combo));
		var indexChanges = 0;
		combo.SelectedIndexChanged += (_, _) => indexChanges++;

		items.RemoveAt(0);
		combo.SelectedIndex.ShouldBe(1); // still "c"
		items.Remove("c");
		combo.SelectedIndex.ShouldBe(-1);
		combo.SelectedItem.ShouldBeNull();
		indexChanges.ShouldBe(2);

		combo.SelectedIndex = 0;
		indexChanges = 0;
		combo.SelectedIndex = 9; // no such item: none, said once, as -1
		combo.SelectedIndex.ShouldBe(-1);
		indexChanges.ShouldBe(1);
		ui.Tick();
	}

	[Test]
	public void A_disabled_combo_box_dims_its_placeholder()
	{
		var combo = new ComboBox { PlaceholderText = "Pick one", IsEnabled = false };
		var ui = new UiDriver(UiDriver.Top(combo));
		ui.Tick();

		UiDriver.Parts<TextBlock>(combo).Single(t => t.Text == "Pick one").Foreground.ShouldBeSameAs(TemplateStates.DisabledForeground);
	}

	// --- RadioButtons, RadioButton ---

	/// <summary>Columns fill top to bottom, then left to right, as WinUI's: Up and Down move to the item before or after,
	/// Left and Right to the next column.</summary>
	[Test]
	public void Radio_buttons_fill_their_columns_down_and_move_by_the_arrows()
	{
		var choices = new RadioButtons { MaxColumns = 2, ItemsSource = new[] { "a", "b", "c", "d" }, SelectedIndex = 0 };
		var ui = new UiDriver(UiDriver.Top(choices));
		var buttons = UiDriver.Parts<RadioButton>(choices);
		(Grid.GetColumn(buttons[1]), Grid.GetRow(buttons[1])).ShouldBe((0, 1));
		(Grid.GetColumn(buttons[2]), Grid.GetRow(buttons[2])).ShouldBe((1, 0));

		buttons[0].Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.Down);
		choices.SelectedIndex.ShouldBe(1);
		ui.Key(VirtualKey.Right);
		choices.SelectedIndex.ShouldBe(3);
		ui.Key(VirtualKey.Up);
		choices.SelectedIndex.ShouldBe(2);
	}

	[Test]
	public void Radio_buttons_are_one_tab_stop_the_chosen_one()
	{
		var before = new Button { Content = "before" };
		var choices = new RadioButtons { ItemsSource = new[] { "a", "b", "c" } };
		var after = new Button { Content = "after" };
		var ui = new UiDriver(UiDriver.Top(before, choices, after));
		var buttons = UiDriver.Parts<RadioButton>(choices);
		buttons.Select(b => b.IsTabStop).ShouldBe([true, false, false]); // the first while none is chosen

		choices.SelectedIndex = 2;
		buttons.Select(b => b.IsTabStop).ShouldBe([false, false, true]);
		before.Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.Tab);
		ui.Root.FocusManager!.FocusedElement.ShouldBeSameAs(buttons[2]);
		ui.Key(VirtualKey.Tab);
		ui.Root.FocusManager.FocusedElement.ShouldBeSameAs(after);
	}

	[Test]
	public void Radio_buttons_with_a_group_name_are_one_choice_across_the_root()
	{
		var a = new RadioButton { Content = "a", GroupName = "Weather" };
		var b = new RadioButton { Content = "b", GroupName = "Weather" };
		var other = new RadioButton { Content = "other", GroupName = "Race" };
		_ = new UiDriver(UiDriver.Top(UiDriver.Top(a), UiDriver.Top(b, other)));

		a.PerformClick();
		other.PerformClick();
		b.PerformClick();
		(a.IsChecked, b.IsChecked, other.IsChecked).ShouldBe((false, true, true));
	}

	[Test]
	public void A_disabled_checked_radio_button_shows_no_accent()
	{
		var radio = new RadioButton { Content = "a", IsChecked = true, IsEnabled = false };
		var ui = new UiDriver(UiDriver.Top(radio));
		ui.Tick();

		var ring = UiDriver.Parts<Border>(radio)[0];
		ring.BorderBrush.ShouldBeSameAs(TemplateStates.DisabledBorder);
		ring.Child.ShouldBeOfType<Border>().Background.ShouldBeSameAs(TemplateStates.DisabledAccent);
	}

	/// <summary>Items markup adds one by one before the control is in a tree are laid out once, as it joins one.</summary>
	[Test]
	public void Radio_buttons_built_before_their_tree_lay_their_items_out_once()
	{
		var choices = new RadioButtons();
		for (var i = 0; i < 50; i++)
			choices.Items.Add($"item {i}");
		UiDriver.Parts<RadioButton>(choices).ShouldBeEmpty(); // nothing built yet

		_ = new UiDriver(UiDriver.Top(choices));
		UiDriver.Parts<RadioButton>(choices).Count.ShouldBe(50);
	}

	// --- InfoBar ---

	[Test]
	public void An_info_bar_says_why_it_closed_and_may_be_kept_open()
	{
		var bar = new InfoBar { IsOpen = true, Title = "Build failed" };
		var ui = new UiDriver(UiDriver.Top(bar));
		var said = new List<string>();
		var keep = true;
		bar.CloseButtonClick += (_, _) => said.Add("click");
		bar.Closing += (_, e) =>
		{
			said.Add($"closing {e.Reason}");
			e.Cancel = keep;
		};
		bar.Closed += (_, e) => said.Add($"closed {e.Reason}");

		UiDriver.Parts<Button>(bar)[0].PerformClick();
		bar.IsOpen.ShouldBeTrue(); // kept open
		keep = false;
		bar.IsOpen = false;

		said.ShouldBe(["click", "closing CloseButton", "closing Programmatic", "closed Programmatic"]);
		ui.Tick();
	}

	[Test]
	public void An_info_bars_panel_takes_its_severitys_resource()
	{
		var error = new SolidColorBrush(HdrColor.FromRgb(80, 20, 20));
		var bar = new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Error };
		var page = new Border { Child = bar, Resources = new ResourceDictionary() };
		page.Resources.Add("InfoBarErrorSeverityBackgroundBrush", error);
		_ = new UiDriver(UiDriver.Top(page));

		var panel = UiDriver.Parts<Border>(bar)[0];
		panel.Background.ShouldBeSameAs(error);
		bar.Severity = InfoBarSeverity.Success;
		panel.Background.ShouldNotBeSameAs(error);
	}

	// --- ToggleSwitch ---

	[Test]
	public void A_disabled_switch_that_is_on_shows_no_accent()
	{
		var toggle = new ToggleSwitch { IsOn = true, IsEnabled = false };
		var ui = new UiDriver(UiDriver.Top(toggle));
		ui.Tick();

		var track = UiDriver.Parts<Border>(toggle)[0];
		track.Background.ShouldBeSameAs(TemplateStates.DisabledAccent);
		track.BorderBrush.ShouldBeSameAs(TemplateStates.DisabledAccent);
	}

	[Test]
	public void A_switch_raises_Toggled_as_a_plain_event_from_code_or_input()
	{
		var toggle = new ToggleSwitch();
		var ui = new UiDriver(UiDriver.Top(toggle));
		var toggled = 0;
		toggle.Toggled += (sender, _) => toggled += ReferenceEquals(sender, toggle) ? 1 : 0;

		toggle.IsOn = true;
		ui.Click(toggle);
		toggled.ShouldBe(2);
	}

	// --- Right-clicks ---

	/// <summary>A right or middle click is left to what's around a button (a context menu), as WinUI's: it never clicks.</summary>
	[Test]
	public void A_right_click_never_clicks_a_button_or_flips_a_switch()
	{
		var button = new Button { Content = "Go" };
		var toggle = new ToggleSwitch();
		var ui = new UiDriver(UiDriver.Top(button, toggle));
		var clicks = 0;
		button.Click += (_, _) => clicks++;

		ui.Click(button, right: true);
		ui.Click(toggle, right: true);
		clicks.ShouldBe(0);
		toggle.IsOn.ShouldBeFalse();
	}

	// --- GridSplitter ---

	private static (Grid Grid, GridSplitter Splitter) Columns(float minimum)
	{
		var grid = new Grid { Width = 400f, Height = 100f };
		grid.ColumnDefinitions.Add(GridLength.Pixels(150f));
		grid.ColumnDefinitions.Add(GridLength.Pixels(8f));
		grid.ColumnDefinitions.Add(GridLength.Star);
		var splitter = new GridSplitter { MinimumTrackSize = minimum };
		Grid.SetColumn(splitter, 1);
		grid.Children.Add(splitter);
		return (grid, splitter);
	}

	[Test]
	public void A_grid_splitter_moves_columns_no_further_than_their_least_and_says_so_once()
	{
		var (grid, splitter) = Columns(minimum: 100f);
		var ui = new UiDriver(UiDriver.Top(grid));
		var resized = 0;
		splitter.Resized += (_, _) => resized++;
		var start = UiDriver.Middle(splitter);

		ui.Click(start, start - new Vector2(120f, 0f));
		grid.ColumnDefinitions[0].Value.ShouldBe(100f, 0.5f); // held at its least
		grid.ColumnDefinitions[2].ShouldBe(GridLength.Star); // the star still takes up the rest
		resized.ShouldBe(1);
	}

	[Test]
	public void A_grid_splitter_leaves_a_star_row_a_star_so_the_grid_still_fits_when_it_shrinks()
	{
		var grid = new Grid { Width = 100f, Height = 400f };
		grid.RowDefinitions.Add(GridLength.Star);
		grid.RowDefinitions.Add(GridLength.Pixels(8f));
		grid.RowDefinitions.Add(GridLength.Pixels(100f));
		var splitter = new GridSplitter { MinimumTrackSize = 24f };
		Grid.SetRow(splitter, 1);
		grid.Children.Add(splitter);
		var ui = new UiDriver(UiDriver.Top(grid));
		var start = UiDriver.Middle(splitter);

		ui.Click(start, start - new Vector2(0f, 50f));
		grid.RowDefinitions[0].ShouldBe(GridLength.Star);
		grid.RowDefinitions[2].Value.ShouldBe(150f, 0.5f);

		grid.Height = 300f;
		ui.Tick();
		splitter.LayoutRect.Bottom.ShouldBeLessThanOrEqualTo(300f - 150f + 0.5f); // the star gave up the room, not the grid's foot
	}

	[Test]
	public void A_grid_splitter_between_two_stars_reweights_them()
	{
		var grid = new Grid { Width = 408f, Height = 100f };
		grid.ColumnDefinitions.Add(GridLength.Star);
		grid.ColumnDefinitions.Add(GridLength.Pixels(8f));
		grid.ColumnDefinitions.Add(GridLength.Star);
		var splitter = new GridSplitter();
		Grid.SetColumn(splitter, 1);
		grid.Children.Add(splitter);
		var ui = new UiDriver(UiDriver.Top(grid));
		var start = UiDriver.Middle(splitter);

		ui.Click(start, start + new Vector2(100f, 0f));
		grid.ColumnDefinitions[0].IsStar.ShouldBeTrue();
		grid.ColumnDefinitions[2].IsStar.ShouldBeTrue();
		grid.ColumnDefinitions[0].Value.ShouldBe(1.5f, 0.01f); // 300 of 400
		grid.ColumnDefinitions[2].Value.ShouldBe(0.5f, 0.01f);
	}

	[Test]
	public void A_grid_splitter_ends_its_drag_when_capture_is_lost_and_ignores_a_right_drag()
	{
		var (grid, splitter) = Columns(minimum: 24f);
		var ui = new UiDriver(UiDriver.Top(grid));
		var start = UiDriver.Middle(splitter);

		ui.Click(start, start + new Vector2(40f, 0f), right: true);
		grid.ColumnDefinitions[0].Value.ShouldBe(150f);

		ui.Host.QueuePointer(PointerInput.Press(start));
		ui.Tick();
		splitter.ReleasePointerCapture(0);
		ui.Host.QueuePointer(PointerInput.Move(start + new Vector2(40f, 0f)));
		ui.Tick();
		grid.ColumnDefinitions[0].Value.ShouldBe(150f); // a hover after the capture went moves nothing
	}

	// --- ScrollViewer ---

	[Test]
	public void A_scroll_viewer_tells_of_a_diagonal_move_once_and_not_of_layout()
	{
		var content = new Border { Width = 600f, Height = 600f, Background = new SolidColorBrush(HdrColor.FromRgb(1, 1, 1)) };
		var viewer = new ScrollViewer
		{
			Width = 200f,
			Height = 100f,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = content,
		};
		var ui = new UiDriver(UiDriver.Top(viewer));
		var moves = 0;
		viewer.ViewChanged += (_, _) => moves++;

		ui.Wheel(viewer, new Vector2(-1f, -1f));
		moves.ShouldBe(1);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
		viewer.VerticalOffset.ShouldBeGreaterThan(0f);

		content.Height = 50f; // the extent shrinks under the offset: layout holds it inside, and says nothing
		ui.Tick();
		viewer.VerticalOffset.ShouldBe(0f);
		moves.ShouldBe(1);
	}
}
