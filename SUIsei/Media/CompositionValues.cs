using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// The element layer's values as the composition layer takes them, where an element or a brush pushes them into its
/// retained composition objects. The composition enums mirror WinUI's <c>Microsoft.UI.Composition</c> ones rather than
/// the XAML enums, so the composition contract names no element-layer type. A value outside its enum maps to the
/// composition default.
/// </summary>
internal static class CompositionValues
{
	extension(PenLineCap cap)
	{
		/// <summary>The cap as composition strokes take it.</summary>
		internal CompositionStrokeCap ToComposition() => cap switch
		{
			PenLineCap.Square => CompositionStrokeCap.Square,
			PenLineCap.Round => CompositionStrokeCap.Round,
			PenLineCap.Triangle => CompositionStrokeCap.Triangle,
			_ => CompositionStrokeCap.Flat,
		};
	}

	extension(PenLineJoin join)
	{
		/// <summary>The join as composition strokes take it.</summary>
		internal CompositionStrokeLineJoin ToComposition() => join switch
		{
			PenLineJoin.Bevel => CompositionStrokeLineJoin.Bevel,
			PenLineJoin.Round => CompositionStrokeLineJoin.Round,
			_ => CompositionStrokeLineJoin.Miter,
		};
	}

	extension(FillRule rule)
	{
		/// <summary>The fill rule as a composition path takes it.</summary>
		internal CompositionFillRule ToComposition() =>
			rule == FillRule.Nonzero ? CompositionFillRule.Nonzero : CompositionFillRule.EvenOdd;
	}

	extension(SweepDirection direction)
	{
		/// <summary>The sweep as a composition path's arc takes it.</summary>
		internal CompositionSweepDirection ToComposition() =>
			direction == SweepDirection.Clockwise ? CompositionSweepDirection.Clockwise : CompositionSweepDirection.Counterclockwise;
	}

	extension(Stretch stretch)
	{
		/// <summary>The stretch as a surface brush takes it.</summary>
		internal CompositionStretch ToComposition() => stretch switch
		{
			Stretch.None => CompositionStretch.None,
			Stretch.Uniform => CompositionStretch.Uniform,
			Stretch.UniformToFill => CompositionStretch.UniformToFill,
			_ => CompositionStretch.Fill,
		};
	}
}
