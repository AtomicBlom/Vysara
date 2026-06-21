using System.Numerics;

using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="TextBox"/> editing model (PlanningUI.md MU6), driven through the real input pipeline: focus the box,
/// queue text/keys, tick. Covers insert, Backspace, Shift-selection + replace, cut/paste, and TextChanged.
/// </summary>
public sealed class TextBoxTests
{
	private static readonly Vector2 Surface = new(300f, 300f);

	private static (UIHost Host, TextBox Box) Focused()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UIPlacement.Overlay(0, 1f));
		var box = new TextBox { Width = 200f };
		view.Root.Content = box;
		host.Tick(0.016, 0f, Surface);
		host.Focus.TrySetFocus(box, FocusState.Programmatic);
		return (host, box);
	}

	private static void Type(UIHost host, string text)
	{
		host.QueueText(text);
		host.Tick(0.016, 0f, Surface);
	}

	private static void Press(UIHost host, VirtualKey key, ModifierKeys modifiers = ModifierKeys.None)
	{
		host.QueueKey(new KeyInput(key, true, false, modifiers, 0));
		host.Tick(0.016, 0f, Surface);
	}

	[Test]
	public void Typing_inserts_at_the_caret()
	{
		var (host, box) = Focused();

		Type(host, "hi");

		box.Text.ShouldBe("hi");
		box.CaretIndex.ShouldBe(2);
	}

	[Test]
	public void Backspace_deletes_before_the_caret()
	{
		var (host, box) = Focused();
		Type(host, "abc");

		Press(host, VirtualKey.Back);

		box.Text.ShouldBe("ab");
		box.CaretIndex.ShouldBe(2);
	}

	[Test]
	public void Shift_arrow_selects_and_typing_replaces_the_selection()
	{
		var (host, box) = Focused();
		Type(host, "hello");

		Press(host, VirtualKey.Left, ModifierKeys.Shift);
		Press(host, VirtualKey.Left, ModifierKeys.Shift);
		box.SelectionLength.ShouldBe(2);

		Type(host, "p");
		box.Text.ShouldBe("help");
	}

	[Test]
	public void Cut_then_paste_round_trips_through_the_clipboard()
	{
		var (host, box) = Focused();
		Type(host, "abcdef");

		box.SelectAll();
		Press(host, VirtualKey.X, ModifierKeys.Control);
		box.Text.ShouldBe("");

		Press(host, VirtualKey.V, ModifierKeys.Control);
		box.Text.ShouldBe("abcdef");
	}

	[Test]
	public void TextChanged_fires_on_edit()
	{
		var (host, box) = Focused();
		var count = 0;
		box.TextChanged += (_, _) => count++;

		Type(host, "x");

		count.ShouldBeGreaterThan(0);
	}
}
