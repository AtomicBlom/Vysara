using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara.Media;

/// <summary>A brush that paints content scaled into the area painted, as WinUI's <c>TileBrush</c>: the base of
/// <see cref="ImageBrush"/>.</summary>
public abstract class TileBrush : Brush
{
	public static readonly DependencyProperty<TileBrush, Stretch> StretchProperty =
		DependencyProperty.Register(nameof(Stretch),
			new PropertyMetadata<TileBrush, Stretch>
			{
				DefaultValue = Stretch.Fill,
				PropertyChanged = static (b, _, value) =>
				{
					b.Surface.Stretch = value.ToComposition();
					b.Changed();
				},
			});

	public static readonly DependencyProperty<TileBrush, AlignmentX> AlignmentXProperty =
		DependencyProperty.Register(nameof(AlignmentX),
			new PropertyMetadata<TileBrush, AlignmentX>
			{
				DefaultValue = AlignmentX.Center,
				PropertyChanged = static (b, _, value) =>
				{
					b.Surface.HorizontalAlignmentRatio = value switch { AlignmentX.Left => 0f, AlignmentX.Right => 1f, _ => 0.5f };
					b.Changed();
				},
			});

	public static readonly DependencyProperty<TileBrush, AlignmentY> AlignmentYProperty =
		DependencyProperty.Register(nameof(AlignmentY),
			new PropertyMetadata<TileBrush, AlignmentY>
			{
				DefaultValue = AlignmentY.Center,
				PropertyChanged = static (b, _, value) =>
				{
					b.Surface.VerticalAlignmentRatio = value switch { AlignmentY.Top => 0f, AlignmentY.Bottom => 1f, _ => 0.5f };
					b.Changed();
				},
			});

	private protected TileBrush() : base(new CompositionSurfaceBrush())
	{
	}

	/// <summary>How the content is scaled into the area painted. Defaults to filling it.</summary>
	public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }

	/// <summary>Where the content sits across the area when it is narrower or wider. Defaults to the centre.</summary>
	public AlignmentX AlignmentX { get => GetValue(AlignmentXProperty); set => SetValue(AlignmentXProperty, value); }

	/// <summary>Where the content sits down the area when it is shorter or taller. Defaults to the centre.</summary>
	public AlignmentY AlignmentY { get => GetValue(AlignmentYProperty); set => SetValue(AlignmentYProperty, value); }

	private protected CompositionSurfaceBrush Surface => (CompositionSurfaceBrush)CompositionBrush;
}
