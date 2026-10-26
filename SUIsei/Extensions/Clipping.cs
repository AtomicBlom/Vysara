namespace SUIsei.Extensions;

/// <summary>
/// Clipping to an element's own bounds, set in XAML as <c>suisei:Clipping.ClipToBounds="True"</c>. WinUI has no such
/// property: it clips with <c>UIElement.Clip</c>, a geometry. Here the element's composition node clips its subtree to
/// the element's layout bounds. Controls that always clip (ScrollViewer, TextBox) do so whatever this says.
/// </summary>
public sealed class Clipping : DependencyObject
{
	private Clipping()
	{
	}

	/// <summary>Whether the element clips its subtree to its layout bounds. False by default.</summary>
	public static readonly DependencyProperty<Clipping, bool> ClipToBoundsProperty =
		DependencyProperty.RegisterAttached<Clipping, bool>("ClipToBounds",
			new PropertyMetadata<Clipping, bool> { DefaultValue = false, Invalidates = InvalidationFlags.Arrange });

	public static void SetClipToBounds(UIElement element, bool value) => element.SetValue(ClipToBoundsProperty, value);
	public static bool GetClipToBounds(UIElement element) => element.GetValue(ClipToBoundsProperty);
}
