namespace Vysara.Extensions;

/// <summary>
/// Keeps content clear of an on-screen keyboard. Set <c>vysara:InputPane.AvoidsKeyboard="True"</c> on the element whose
/// area the keyboard may cover, typically the page's main <c>Border</c> or <c>Grid</c>: while the keyboard is up, the
/// element is arranged shorter by however much of it lies under the keyboard, so its <c>*</c> rows, a
/// <c>ScrollViewer</c> or anything stretched inside it shrinks to the part still visible, and a text field at its bottom
/// rises above the keyboard. Elements without it are left where they are, under the keyboard if that is where they are.
/// </summary>
/// <remarks>
/// The keyboard's height comes from the root: the host sets <see cref="UIRoot.InputPaneHeight"/> (in surface pixels) to
/// the bottom part of the surface its on-screen keyboard covers, and to zero when the keyboard goes. The element's
/// position is taken from its arranged ancestors, so a render transform or a scroll offset above it is not accounted for.
/// </remarks>
public sealed class InputPane : DependencyObject
{
	private InputPane()
	{
	}

	/// <summary>Whether the element is arranged shorter to keep clear of an on-screen keyboard. False by default.</summary>
	public static readonly DependencyProperty<InputPane, bool> AvoidsKeyboardProperty =
		DependencyProperty.RegisterAttached<InputPane, bool>("AvoidsKeyboard",
			new PropertyMetadata<InputPane, bool> { DefaultValue = false, Invalidates = InvalidationFlags.Arrange });

	public static void SetAvoidsKeyboard(UIElement element, bool value) => element.SetValue(AvoidsKeyboardProperty, value);
	public static bool GetAvoidsKeyboard(UIElement element) => element.GetValue(AvoidsKeyboardProperty);

	/// <summary>
	/// The slot <paramref name="element"/> is arranged into, shortened by the part of it that lies under its root's
	/// on-screen keyboard: <paramref name="slot"/> itself when it opts out, is not in a live tree, or clears the keyboard.
	/// </summary>
	internal static Rect Avoid(UIElement element, Rect slot)
	{
		if (element.AttachedRoot is not { InputPaneTop: { } keyboardTop } || !GetAvoidsKeyboard(element))
			return slot;

		// The slot is in the parent's space; each arranged ancestor's placement takes it on to the root's.
		var top = slot.Y;
		for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
			top += ancestor.LayoutRect.Y;
		// The element's own box ends a bottom margin above the slot's end; only that much of it can be covered.
		var bottomMargin = element is FrameworkElement framework ? framework.Margin.Bottom : 0f;
		var covered = top + slot.Height - bottomMargin - keyboardTop;
		return covered <= 0f ? slot : new Rect(slot.X, slot.Y, slot.Width, MathF.Max(0f, slot.Height - covered));
	}
}
