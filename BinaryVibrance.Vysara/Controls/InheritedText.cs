using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// The text properties WinUI passes down the tree, <c>FontSize</c>, <c>FontFamily</c>, <c>FontWeight</c> and <c>Foreground</c>, and the dependency property each
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

	public static readonly InheritedProperty<FontFamily?> FontFamily = new(static element => element switch
	{
		TextBlock => TextBlock.FontFamilyProperty,
		ContentPresenter => ContentPresenter.FontFamilyProperty,
		Control => Control.FontFamilyProperty,
		_ => null,
	});

	public static readonly InheritedProperty<FontWeight> FontWeight = new(static element => element switch
	{
		TextBlock => TextBlock.FontWeightProperty,
		ContentPresenter => ContentPresenter.FontWeightProperty,
		Control => Control.FontWeightProperty,
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

	/// <summary>The colour of the text brush the built-in looks give their controls: white, Vysara's default text colour.</summary>
	public static readonly HdrColor BuiltInForeground = HdrColor.FromRgb(255, 255, 255);

	/// <summary>
	/// Give <paramref name="control"/> the text its built-in look sets, at the BuiltInStyle level, as WinUI's default styles
	/// set a Button's or a TextBox's <c>FontSize</c> and <c>Foreground</c> with setters: those outrank inheritance, so a
	/// page's text properties reach its text blocks but not its controls, which keep their own unless set on them. The
	/// foreground is a brush of <paramref name="foreground"/> (<see cref="BuiltInForeground"/> when omitted) made for this
	/// control alone, rather than one sealed and shared: its template's text takes it, and a visual state that animates
	/// <c>(ContentPresenter.Foreground).(SolidColorBrush.Color)</c> recolours it in place, as WinUI templates do, without
	/// recolouring any other control.
	/// </summary>
	public static void GiveBuiltInLook(Control control, HdrColor? foreground = null)
	{
		control.SetBuiltInValue(Control.FontSizeProperty, 14f);
		control.SetBuiltInValue(Control.ForegroundProperty, new SolidColorBrush(foreground ?? BuiltInForeground));
	}

	/// <summary><paramref name="child"/> joined <paramref name="parent"/>: it takes the text properties the parent passes
	/// down.</summary>
	public static void Adopt(UIElement parent, UIElement child)
	{
		FontSize.Adopt(parent, child);
		FontFamily.Adopt(parent, child);
		FontWeight.Adopt(parent, child);
		Foreground.Adopt(parent, child);
	}

	/// <summary><paramref name="child"/> left its parent: it gives up the text properties it took from above.</summary>
	public static void Drop(UIElement child)
	{
		FontSize.Drop(child);
		FontFamily.Drop(child);
		FontWeight.Drop(child);
		Foreground.Drop(child);
	}
}
