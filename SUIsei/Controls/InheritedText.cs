using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The text properties WinUI passes down the tree, <c>FontSize</c> and <c>Foreground</c>, and the dependency property each
/// element holds them as: a <see cref="TextBlock"/>'s own, a <see cref="ContentPresenter"/>'s or a <see cref="Control"/>'s.
/// So text in a control's template, or made for its string content, is sized and painted by the presenter or control
/// above it unless it sets its own. A built-in control's look sets its own (<see cref="GiveBuiltInLook"/>), as WinUI's
/// default styles do, so a page's text properties don't reach it.
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

	/// <summary>Reject a text foreground the compositor can't paint glyphs with, where it is set, with an
	/// <see cref="ArgumentException"/>: an image brush, since glyphs are drawn from the font atlas and a glyph prim samples
	/// one texture.</summary>
	public static void ValidateForeground(Brush? brush)
	{
		if (brush is TileBrush)
			throw new ArgumentException(
				"An image brush cannot paint text: glyphs are drawn from the font atlas, and a glyph prim samples one texture.", "value");
	}

	/// <summary>The text brush the built-in looks give their controls: white, SUIsei's default text colour.</summary>
	public static readonly SolidColorBrush BuiltInForeground = Brush.Sealed(HdrColor.FromRgb(255, 255, 255));

	/// <summary>
	/// Give <paramref name="control"/> the text its built-in look sets, at the BuiltInStyle level, as WinUI's default styles
	/// set a Button's or a TextBox's <c>FontSize</c> and <c>Foreground</c> with setters: those outrank inheritance, so a
	/// page's text properties reach its text blocks but not its controls, which keep their own unless set on them.
	/// </summary>
	public static void GiveBuiltInLook(Control control)
	{
		control.SetBuiltInValue(Control.FontSizeProperty, 14f);
		control.SetBuiltInValue(Control.ForegroundProperty, BuiltInForeground);
	}

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
