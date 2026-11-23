using Vysara.Controls.Primitives;
using Vysara.Media;

namespace Vysara.Controls;

/// <summary>
/// A flyout holding any <see cref="Content"/>, as WinUI's <c>Flyout</c>: shown on a rounded panel against its target
/// (<see cref="FlyoutBase"/>). The panel's look comes from the <c>FlyoutPresenterBackground</c> and
/// <c>FlyoutBorderThemeBrush</c> resources found from the target up, as WinUI's lightweight styling does.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed class Flyout : FlyoutBase
{
	private static readonly SolidColorBrush PanelBackground = Brush.Sealed(HdrColor.FromRgb(40, 42, 52));
	private static readonly SolidColorBrush PanelBorder = Brush.Sealed(HdrColor.FromRgb(78, 82, 96));
	public static readonly DependencyProperty<Flyout, UIElement?> ContentProperty =
		DependencyProperty.Register(nameof(Content), new PropertyMetadata<Flyout, UIElement?>());

	/// <summary>What the flyout shows.</summary>
	public UIElement? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

	protected override FrameworkElement CreatePresenter()
	{
		var target = Target!;
		// The content leaves the panel it was last shown in, so it can join this one
		if (Content is { Parent: Border previous } && ReferenceEquals(previous.Child, Content))
			previous.Child = null;
		return new Border
		{
			Background = Resource(target, "FlyoutPresenterBackground", PanelBackground),
			BorderBrush = Resource(target, "FlyoutBorderThemeBrush", PanelBorder),
			BorderThickness = new Thickness(1f),
			CornerRadius = new CornerRadius(8f),
			Padding = new Thickness(14f, 12f, 14f, 12f),
			Child = Content,
		};
	}
}
