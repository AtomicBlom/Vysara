using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The text properties WinUI passes down the tree, <c>FontSize</c> and <c>Foreground</c>, and the dependency property each
/// element holds them as: a <see cref="TextBlock"/>'s own, a <see cref="ContentPresenter"/>'s or a <see cref="Control"/>'s.
/// So text in a control's template, or made for its string content, is sized and painted by the presenter or control
/// above it unless it sets its own, and a control passes its values on to the controls inside it.
/// </summary>
internal static class InheritedText
{
	public static readonly InheritedProperty<float> FontSize = new(static element => element switch
	{
		TextBlock => TextBlock.FontSizeProperty,
		ContentPresenter => ContentPresenter.FontSizeProperty,
		Control => Control.FontSizeProperty,
		_ => null,
	});

	public static readonly InheritedProperty<Brush?> Foreground = new(static element => element switch
	{
		TextBlock => TextBlock.ForegroundProperty,
		ContentPresenter => ContentPresenter.ForegroundProperty,
		Control => Control.ForegroundProperty,
		_ => null,
	});

	/// <summary><paramref name="child"/> joined <paramref name="parent"/>: it takes the text properties the parent passes
	/// down.</summary>
	public static void Adopt(UIElement parent, UIElement child)
	{
		FontSize.Adopt(parent, child);
		Foreground.Adopt(parent, child);
	}

	/// <summary><paramref name="child"/> left its parent: it gives up the text properties it took from above.</summary>
	public static void Drop(UIElement child)
	{
		FontSize.Drop(child);
		Foreground.Drop(child);
	}
}
