using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>The base of multi-child layout containers: owns the <see cref="Children"/>
/// collection and an optional <see cref="Background"/>. Subclasses implement the measure/arrange policy.</summary>
[ContentProperty(Name = nameof(Children))]
public abstract class Panel : FrameworkElement
{
	public static readonly DependencyProperty<Panel, Brush?> BackgroundProperty =
		DependencyProperty.Register(nameof(Background),
			new PropertyMetadata<Panel, Brush?>
			{
				PropertyChanged = static (p, old, @new) =>
				{
					p.TransitionBackground(p.BackgroundTransition, old, @new);
					p.OnBrushChanged(old, @new);
				},
			});

	protected Panel() => Children = new UIElementCollection(this);

	public UIElementCollection Children { get; }

	/// <summary>An optional brush behind the children (hit-testing makes a backgroundless panel transparent).</summary>
	public Brush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

	/// <summary>How a change of <see cref="Background"/> animates; null shows it at once.</summary>
	public BrushTransition? BackgroundTransition { get; set; }

	internal override IReadOnlyList<UIElement> VisualChildren => Children.Items;

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Background, attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override VisualContent? UpdateContent() => BackgroundPaint(Background) is { } bg ? RetainedRect(bg) : null;
}
