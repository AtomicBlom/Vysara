using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Tests.Sample;

namespace Vysara.Tests;

/// <summary>
/// Live <c>{x:Bind}</c> end to end over <c>LiveBindingView.xaml</c>: a bool shown as a Visibility, function bindings
/// re-evaluated as their arguments change, TwoWay <c>IsChecked</c> and a Slider's double written back to a float, a source
/// that refuses or clamps a value shown as it is, and a DataTemplate whose OneWay and TwoWay bindings follow their item
/// while its element is in a live tree, and let it go when the element leaves.
/// </summary>
public sealed class LiveBindingTests
{
	private static (LiveBindingView View, UIRoot Root) Live()
	{
		var view = new LiveBindingView();
		var root = new UIRoot(new DrawListId(0)) { Content = view.Root };
		return (view, root);
	}

	// The realized row of choice `index`: its toggle, its level's text, and the panel of its tags.
	private static (ToggleButton Toggle, TextBlock Level, ItemsControl Tags) Row(LiveBindingView view, int index)
	{
		var row = (StackPanel)view.Choices.ItemsHost!.Children[index];
		return ((ToggleButton)row.Children[0], (TextBlock)row.Children[1], (ItemsControl)row.Children[2]);
	}

	[Test]
	public void A_bool_shows_as_a_visibility()
	{
		var (view, _) = Live();
		view.Banner.Visibility.ShouldBe(Visibility.Collapsed);

		view.ViewModel.HasError = true;
		view.Banner.Visibility.ShouldBe(Visibility.Visible);
	}

	[Test]
	public void A_function_binding_follows_its_arguments_and_their_owners()
	{
		var (view, _) = Live();
		view.Percent.Text.ShouldBe("50%");
		view.Summary.Text.ShouldBe("a 50%");

		view.ViewModel.Level = 0.25f;
		view.Percent.Text.ShouldBe("25%");
		view.Summary.Text.ShouldBe("a 25%");

		// An intermediate of an argument's path changing rewires the binding to the new object.
		view.ViewModel.Choices[2].Select();
		view.Summary.Text.ShouldBe("c 25%");
	}

	[Test]
	public void IsChecked_writes_back_through_IsCheckedChanged()
	{
		var (view, _) = Live();
		view.Loop.IsChecked.ShouldBe(false);

		view.Loop.PerformClick();
		view.ViewModel.Loop.ShouldBeTrue();

		view.ViewModel.Loop = false;
		view.Loop.IsChecked.ShouldBe(false);
	}

	[Test]
	public void A_slider_writes_a_float_back_and_shows_the_value_the_source_clamped_it_to()
	{
		var (view, _) = Live();
		view.Gain.Value.ShouldBe(0.5, 1e-6);

		view.Gain.Value = 0.3;
		view.ViewModel.Level.ShouldBe(0.3f, 1e-6f);

		view.Gain.Value = 1.0;
		view.ViewModel.Level.ShouldBe(0.8f);
		view.Gain.Value.ShouldBe(0.8, 1e-6);
	}

	[Test]
	public void Template_bindings_follow_their_item()
	{
		var (view, _) = Live();
		var (_, level, _) = Row(view, 1);
		level.Text.ShouldBe("25%");
		level.Visibility.ShouldBe(Visibility.Collapsed);

		view.ViewModel.Choices[1].Level = 0.75f;
		level.Text.ShouldBe("75%");

		view.ViewModel.Choices[1].Select();
		level.Visibility.ShouldBe(Visibility.Visible);
		Row(view, 0).Level.Visibility.ShouldBe(Visibility.Collapsed);
	}

	[Test]
	public void A_radio_like_choice_stays_checked_when_its_source_refuses_to_unselect()
	{
		var (view, _) = Live();
		var first = Row(view, 0).Toggle;
		var second = Row(view, 1).Toggle;
		first.IsChecked.ShouldBe(true);

		first.PerformClick(); // toggles it off; the source refuses, and the binding shows it checked again
		first.IsChecked.ShouldBe(true);
		view.ViewModel.Choices[0].IsSelected.ShouldBeTrue();

		second.PerformClick();
		second.IsChecked.ShouldBe(true);
		first.IsChecked.ShouldBe(false);
		view.ViewModel.Selected.ShouldBe(view.ViewModel.Choices[1]);
	}

	[Test]
	public void A_template_nested_in_a_template_binds_its_string_items()
	{
		var (view, _) = Live();
		var tags = Row(view, 2).Tags;

		tags.ItemsHost!.Children.Count.ShouldBe(2);
		((TextBlock)tags.ItemsHost.Children[1]).Text.ShouldBe("c2");
	}

	[Test]
	public void A_removed_row_lets_its_item_go()
	{
		var (view, _) = Live();
		var choice = view.ViewModel.Choices[1];
		var level = Row(view, 1).Level;

		view.ViewModel.Choices.RemoveAt(1);
		choice.Level = 0.5f;

		level.Text.ShouldBe("25%"); // detached with its row: no longer following the item
	}

	[Test]
	public void Rows_follow_their_items_only_while_in_a_live_tree_and_catch_up_on_joining()
	{
		var view = new LiveBindingView();
		var level = Row(view, 1).Level;
		level.Text.ShouldBe("25%"); // pushed once as the row is built

		view.ViewModel.Choices[1].Level = 0.5f;
		level.Text.ShouldBe("25%"); // not live: not listening

		var root = new UIRoot(new DrawListId(0)) { Content = view.Root };
		level.Text.ShouldBe("50%"); // joining pushes the item's values again

		view.ViewModel.Choices[1].Level = 0.75f;
		level.Text.ShouldBe("75%");

		root.Content = null;
		view.ViewModel.Choices[1].Level = 0.1f;
		level.Text.ShouldBe("75%");
	}
}
