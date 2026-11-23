using Vysara.Controls;
using Vysara.Media;

namespace Vysara.Tests;

/// <summary>
/// The tree walked and changed from outside it: <see cref="VisualTreeHelper"/>'s parent and children, and a panel's
/// <see cref="UIElementCollection"/> finding and removing a child as WinUI's does.
/// </summary>
public sealed class VisualTreeTests
{
	[Test]
	public void The_helper_walks_up_and_down_a_tree()
	{
		var leaf = new Border();
		var inner = new StackPanel();
		inner.Children.Add(leaf);
		var outer = new Grid();
		outer.Children.Add(new TextBlock());
		outer.Children.Add(inner);

		VisualTreeHelper.GetParent(leaf).ShouldBe(inner);
		VisualTreeHelper.GetParent(inner).ShouldBe(outer);
		VisualTreeHelper.GetParent(outer).ShouldBeNull();
		VisualTreeHelper.GetChildrenCount(outer).ShouldBe(2);
		VisualTreeHelper.GetChild(outer, 1).ShouldBe(inner);
		Should.Throw<ArgumentOutOfRangeException>(() => VisualTreeHelper.GetChild(outer, 2));
	}

	[Test]
	public void A_child_is_found_and_removed_by_itself()
	{
		var panel = new StackPanel();
		var first = new Border();
		var second = new Border();
		panel.Children.Add(first);
		panel.Children.Add(second);

		panel.Children.IndexOf(second).ShouldBe(1);
		panel.Children.Contains(first).ShouldBeTrue();
		panel.Children.Remove(first).ShouldBeTrue();
		VisualTreeHelper.GetParent(first).ShouldBeNull();
		panel.Children.Count.ShouldBe(1);
		panel.Children.IndexOf(second).ShouldBe(0);
		panel.Children.Remove(first).ShouldBeFalse();
	}
}
