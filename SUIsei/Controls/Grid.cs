namespace SUIsei.Controls;

/// <summary>
/// A row/column grid: Pixel tracks are fixed, Auto tracks size to their largest child, and
/// Star tracks share the leftover space by weight after Pixel/Auto are subtracted. Children select a cell with the
/// attached <see cref="RowProperty"/>/<see cref="ColumnProperty"/> and may span tracks with
/// <see cref="RowSpanProperty"/>/<see cref="ColumnSpanProperty"/>. An empty definition list defaults to one Star
/// track. Limitation: a spanning child does not drive Auto-track sizing (only single-cell children do).
/// </summary>
public sealed class Grid : Panel
{
	public static readonly DependencyProperty<Grid, int> RowProperty =
		DependencyProperty.RegisterAttached<Grid, int>("Row",
			new PropertyMetadata<Grid, int> { DefaultValue = 0, Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<Grid, int> ColumnProperty =
		DependencyProperty.RegisterAttached<Grid, int>("Column",
			new PropertyMetadata<Grid, int> { DefaultValue = 0, Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<Grid, int> RowSpanProperty =
		DependencyProperty.RegisterAttached<Grid, int>("RowSpan",
			new PropertyMetadata<Grid, int> { DefaultValue = 1, Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<Grid, int> ColumnSpanProperty =
		DependencyProperty.RegisterAttached<Grid, int>("ColumnSpan",
			new PropertyMetadata<Grid, int> { DefaultValue = 1, Invalidates = InvalidationFlags.Measure });

	public static void SetRow(UIElement e, int v) => e.SetValue(RowProperty, v);
	public static int GetRow(UIElement e) => e.GetValue(RowProperty);
	public static void SetColumn(UIElement e, int v) => e.SetValue(ColumnProperty, v);
	public static int GetColumn(UIElement e) => e.GetValue(ColumnProperty);
	public static void SetRowSpan(UIElement e, int v) => e.SetValue(RowSpanProperty, v);
	public static int GetRowSpan(UIElement e) => e.GetValue(RowSpanProperty);
	public static void SetColumnSpan(UIElement e, int v) => e.SetValue(ColumnSpanProperty, v);
	public static int GetColumnSpan(UIElement e) => e.GetValue(ColumnSpanProperty);

	/// <summary>Column track lengths (empty ⇒ a single Star column).</summary>
	public IList<GridLength> ColumnDefinitions { get; } = [];

	/// <summary>Row track lengths (empty ⇒ a single Star row).</summary>
	public IList<GridLength> RowDefinitions { get; } = [];

	private float[] _colSizes = [];
	private float[] _rowSizes = [];

	private GridLength[] Cols => ColumnDefinitions.Count > 0 ? [.. ColumnDefinitions] : [GridLength.Star];
	private GridLength[] Rows => RowDefinitions.Count > 0 ? [.. RowDefinitions] : [GridLength.Star];

	protected override Size MeasureOverride(Size available)
	{
		var cols = Cols;
		var rows = Rows;

		// Measure each child exactly once, against a cell constraint derived from the track types it spans: a Pixel
		// track contributes its fixed extent; an Auto or Star track is left unconstrained, so the child reports its
		// content size (which sizes Auto/Star tracks, with Star taking its proportional share only at arrange). One
		// measure per child — no second resolved-size pass — so constraint-dependent content cannot desire one size
		// for the track and a different one for its final cell.
		foreach (var child in Children.Items)
		{
			var ci = ClampIndex(GetColumn(child), cols.Length);
			var ri = ClampIndex(GetRow(child), rows.Length);
			child.Measure(new Size(
				CellConstraint(cols, ci, GetColumnSpan(child)),
				CellConstraint(rows, ri, GetRowSpan(child))));
		}

		_colSizes = ResolveTracks(cols, available.Width, horizontal: true);
		_rowSizes = ResolveTracks(rows, available.Height, horizontal: false);
		return new Size(Sum(_colSizes), Sum(_rowSizes));
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		// Re-resolve against the size we're actually arranged into, not the (often infinite, when this grid sits in
		// an unconstrained ancestor cell) size Measure saw — otherwise a Star track that measured content-sized under
		// an infinite constraint never claims the real leftover space it's entitled to at arrange time. Auto/Pixel
		// tracks are unaffected (ResolveTracks only reads children's already-computed DesiredSize; it never remeasures).
		_colSizes = ResolveTracks(Cols, finalSize.Width, horizontal: true);
		_rowSizes = ResolveTracks(Rows, finalSize.Height, horizontal: false);

		var xs = Offsets(_colSizes);
		var ys = Offsets(_rowSizes);
		foreach (var child in Children.Items)
		{
			var ci = Math.Clamp(GetColumn(child), 0, _colSizes.Length - 1);
			var ri = Math.Clamp(GetRow(child), 0, _rowSizes.Length - 1);
			var w = SpanSum(_colSizes, ci, GetColumnSpan(child));
			var h = SpanSum(_rowSizes, ri, GetRowSpan(child));
			child.Arrange(new Rect(xs[ci], ys[ri], w, h));
		}
		return finalSize;
	}

	// Sum the sizes of the (clamped) run of tracks a child spans starting at <paramref name="start"/>.
	private static float SpanSum(float[] sizes, int start, int span)
	{
		var end = Math.Min(sizes.Length, start + Math.Max(1, span));
		var s = 0f;
		for (var i = start; i < end; i++)
			s += sizes[i];
		return s;
	}

	// Resolve one axis: Pixel fixed, Auto = max child desired in that track, Star = proportional share of the leftover
	// when the axis is finite — and content-sized (like Auto) when it is infinite. Reads the children's already-
	// computed DesiredSize; it never measures.
	private float[] ResolveTracks(GridLength[] defs, float available, bool horizontal)
	{
		var sizes = new float[defs.Length];
		float used = 0f, starWeight = 0f;
		var finite = !float.IsPositiveInfinity(available);

		for (var i = 0; i < defs.Length; i++)
		{
			var d = defs[i];
			if (d.IsPixel) { sizes[i] = d.Value; used += d.Value; }
			else if (d.IsAuto) { sizes[i] = AutoExtent(i, horizontal); used += sizes[i]; }
			else starWeight += d.Value;
		}

		if (starWeight <= 0f)
			return sizes;

		if (finite)
		{
			var leftover = MathF.Max(0f, available - used);
			for (var i = 0; i < defs.Length; i++)
				if (defs[i].IsStar)
					sizes[i] = leftover * (defs[i].Value / starWeight);
		}
		else
		{
			// Under an infinite constraint there is no leftover to divide, so a Star track sizes to its content,
			// exactly like Auto (matching WinUI). Without this every Star track resolves to zero, and a default
			// (single-Star) Grid measured unconstrained — a StackPanel's stacking axis, a Canvas — would desire
			// nothing and render nothing.
			for (var i = 0; i < defs.Length; i++)
				if (defs[i].IsStar)
					sizes[i] = AutoExtent(i, horizontal);
		}
		return sizes;
	}

	// Max desired extent of the single-cell children in a track. Children were measured once in MeasureOverride
	// (Auto/Star cells unconstrained), so this only reads DesiredSize. Spanning children are excluded — a span's
	// desired size is not distributed across its Auto tracks (documented).
	private float AutoExtent(int track, bool horizontal)
	{
		var max = 0f;
		foreach (var child in Children.Items)
		{
			var t = horizontal ? GetColumn(child) : GetRow(child);
			var span = Math.Max(1, horizontal ? GetColumnSpan(child) : GetRowSpan(child));
			if (t != track || span != 1)
				continue;
			max = MathF.Max(max, horizontal ? child.DesiredSize.Width : child.DesiredSize.Height);
		}
		return max;
	}

	// The measure constraint for a child's cell: the summed extent of the Pixel tracks it spans, or unbounded if any
	// spanned track is Auto/Star (so the child reports its content size for that track's sizing).
	private static float CellConstraint(GridLength[] defs, int start, int span)
	{
		var end = Math.Min(defs.Length, start + Math.Max(1, span));
		var sum = 0f;
		for (var i = start; i < end; i++)
		{
			if (!defs[i].IsPixel)
				return float.PositiveInfinity;
			sum += defs[i].Value;
		}
		return sum;
	}

	private static int ClampIndex(int value, int length) => Math.Clamp(value, 0, length - 1);

	private static float Sum(float[] a) { float s = 0f; foreach (var v in a) s += v; return s; }

	private static float[] Offsets(float[] sizes)
	{
		var o = new float[sizes.Length];
		float acc = 0f;
		for (var i = 0; i < sizes.Length; i++) { o[i] = acc; acc += sizes[i]; }
		return o;
	}
}
