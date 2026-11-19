using System.Runtime.InteropServices;

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

	// The definitions, kept as lists so layout reads them as spans rather than copying them on every pass.
	private readonly List<GridLength> _columnDefinitions = [];
	private readonly List<GridLength> _rowDefinitions = [];

	/// <summary>Column track lengths (empty ⇒ a single Star column).</summary>
	public IList<GridLength> ColumnDefinitions => _columnDefinitions;

	/// <summary>Row track lengths (empty ⇒ a single Star row).</summary>
	public IList<GridLength> RowDefinitions => _rowDefinitions;

	/// <summary>The size a row (or column) was given at the last arrange, in DIPs; 0 for one that doesn't exist.</summary>
	internal float TrackSize(bool rows, int index)
	{
		var sizes = rows ? _rowSizes : _colSizes;
		return index >= 0 && index < sizes.Length ? sizes[index] : 0f;
	}

	// What an empty definition list lays out as.
	private static readonly GridLength[] OneStar = [GridLength.Star];

	// Each track's size and offset, and the largest single-cell child in each track, kept from pass to pass: a buffer is
	// replaced only when the number of tracks changes (the extents, shared by both axes, only when it grows), so a layout
	// pass allocates nothing.
	private float[] _colSizes = [];
	private float[] _rowSizes = [];
	private float[] _colOffsets = [];
	private float[] _rowOffsets = [];
	private float[] _extents = [];

	private ReadOnlySpan<GridLength> Cols => _columnDefinitions.Count > 0 ? CollectionsMarshal.AsSpan(_columnDefinitions) : OneStar;
	private ReadOnlySpan<GridLength> Rows => _rowDefinitions.Count > 0 ? CollectionsMarshal.AsSpan(_rowDefinitions) : OneStar;

	protected override Size MeasureOverride(Size available)
	{
		var cols = Cols;
		var rows = Rows;

		// Measure each child against a cell constraint derived from the track types it spans: a Pixel track contributes its
		// fixed extent; a Star track, where its dimension has no Auto track and the grid is offered a finite extent, its
		// proportional share of what the Pixel tracks leave, as WinUI measures it (so text in a plain grid wraps at the
		// grid's width); otherwise an Auto or Star track is left unconstrained, so the child reports its content size (which
		// sizes Auto/Star tracks, with Star taking its proportional share only at arrange).
		foreach (var child in Children.Items)
		{
			var ci = ClampIndex(GetColumn(child), cols.Length);
			var ri = ClampIndex(GetRow(child), rows.Length);
			child.Measure(new Size(
				CellConstraint(cols, ci, GetColumnSpan(child), available.Width),
				CellConstraint(rows, ri, GetRowSpan(child), available.Height)));
		}

		ResolveTracks(cols, available.Width, horizontal: true, ref _colSizes);
		ResolveTracks(rows, available.Height, horizontal: false, ref _rowSizes);

		// A Star cell beside an Auto track was measured unbounded above, since its share wasn't known until the Auto tracks
		// were sized. As WinUI measures star cells after the Auto ones, it is measured again now for that share: so content
		// that lays out to its width (a scroll viewer deciding on its scroll bars, text wrapping) lays out for the width it
		// will be arranged at, not for unbounded room.
		var acrossWaited = WaitsForAuto(cols, available.Width);
		var downWaited = WaitsForAuto(rows, available.Height);
		if (!acrossWaited && !downWaited)
			return new Size(Sum(_colSizes), Sum(_rowSizes));
		var again = false;
		foreach (var child in Children.Items)
		{
			var ci = ClampIndex(GetColumn(child), cols.Length);
			var ri = ClampIndex(GetRow(child), rows.Length);
			var across = acrossWaited && InStarShare(cols, ci, GetColumnSpan(child));
			var down = downWaited && InStarShare(rows, ri, GetRowSpan(child));
			if (!across && !down)
				continue;
			child.Measure(new Size(
				across ? SpanSum(_colSizes, ci, GetColumnSpan(child)) : CellConstraint(cols, ci, GetColumnSpan(child), available.Width),
				down ? SpanSum(_rowSizes, ri, GetRowSpan(child)) : CellConstraint(rows, ri, GetRowSpan(child), available.Height)));
			again = true;
		}
		if (again)
		{
			ResolveTracks(cols, available.Width, horizontal: true, ref _colSizes);
			ResolveTracks(rows, available.Height, horizontal: false, ref _rowSizes);
		}
		return new Size(Sum(_colSizes), Sum(_rowSizes));
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		// Re-resolve against the size we're actually arranged into, not the (often infinite, when this grid sits in
		// an unconstrained ancestor cell) size Measure saw — otherwise a Star track that measured content-sized under
		// an infinite constraint never claims the real leftover space it's entitled to at arrange time. Auto/Pixel
		// tracks are unaffected (ResolveTracks only reads children's already-computed DesiredSize; it never remeasures).
		ResolveTracks(Cols, finalSize.Width, horizontal: true, ref _colSizes);
		ResolveTracks(Rows, finalSize.Height, horizontal: false, ref _rowSizes);

		Offsets(_colSizes, ref _colOffsets);
		Offsets(_rowSizes, ref _rowOffsets);
		foreach (var child in Children.Items)
		{
			var ci = Math.Clamp(GetColumn(child), 0, _colSizes.Length - 1);
			var ri = Math.Clamp(GetRow(child), 0, _rowSizes.Length - 1);
			var w = SpanSum(_colSizes, ci, GetColumnSpan(child));
			var h = SpanSum(_rowSizes, ri, GetRowSpan(child));
			child.Arrange(new Rect(_colOffsets[ci], _rowOffsets[ri], w, h));
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

	// Resolve one axis into sizes: Pixel fixed, Auto = max child desired in that track, Star = proportional share of
	// the leftover when the axis is finite — and content-sized (like Auto) when it is infinite. Reads the children's
	// already-computed DesiredSize; it never measures.
	private void ResolveTracks(ReadOnlySpan<GridLength> defs, float available, bool horizontal, ref float[] sizes)
	{
		if (sizes.Length != defs.Length)
			sizes = new float[defs.Length];
		else
			Array.Clear(sizes);
		float used = 0f, starWeight = 0f;
		var finite = !float.IsPositiveInfinity(available);
		var extents = NeedsExtents(defs, finite) ? Extents(defs.Length, horizontal) : [];

		for (var i = 0; i < defs.Length; i++)
		{
			var d = defs[i];
			if (d.IsPixel) { sizes[i] = d.Value; used += d.Value; }
			else if (d.IsAuto) { sizes[i] = extents[i]; used += sizes[i]; }
			else starWeight += d.Value;
		}

		if (starWeight <= 0f)
			return;

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
					sizes[i] = extents[i];
		}
	}

	// Whether an axis sizes any track to its content: an Auto track, or a Star track under an infinite constraint.
	private static bool NeedsExtents(ReadOnlySpan<GridLength> defs, bool finite)
	{
		foreach (var d in defs)
			if (d.IsAuto || (!finite && d.IsStar))
				return true;
		return false;
	}

	// Max desired extent of the single-cell children in each track, in one walk over the children. Children were
	// measured once in MeasureOverride (Auto/Star cells unconstrained), so this only reads DesiredSize. Spanning
	// children are excluded — a span's desired size is not distributed across its Auto tracks (documented) — and so is
	// a child whose row or column is out of range, though it is arranged in the nearest track.
	private ReadOnlySpan<float> Extents(int tracks, bool horizontal)
	{
		if (_extents.Length < tracks)
			_extents = new float[tracks];
		var extents = _extents.AsSpan(0, tracks);
		extents.Clear();
		foreach (var child in Children.Items)
		{
			var t = horizontal ? GetColumn(child) : GetRow(child);
			var span = Math.Max(1, horizontal ? GetColumnSpan(child) : GetRowSpan(child));
			if ((uint)t >= (uint)tracks || span != 1)
				continue;
			extents[t] = MathF.Max(extents[t], horizontal ? child.DesiredSize.Width : child.DesiredSize.Height);
		}
		return extents;
	}

	// The measure constraint for a child's cell: the summed extent of the Pixel tracks it spans, or unbounded if any
	// spanned track is Auto/Star (so the child reports its content size for that track's sizing).
	private static float CellConstraint(ReadOnlySpan<GridLength> defs, int start, int span, float available)
	{
		// What a Star track's share is out of: the extent the Pixel tracks leave, when no Auto track takes some first
		float pixels = 0f, stars = 0f;
		var shares = float.IsFinite(available);
		foreach (var d in defs)
		{
			if (d.IsPixel) pixels += d.Value;
			else if (d.IsStar) stars += d.Value;
			else shares = false;
		}
		var end = Math.Min(defs.Length, start + Math.Max(1, span));
		var sum = 0f;
		for (var i = start; i < end; i++)
		{
			if (defs[i].IsPixel)
				sum += defs[i].Value;
			else if (defs[i].IsStar && shares && stars > 0f)
				sum += MathF.Max(0f, available - pixels) * defs[i].Value / stars;
			else
				return float.PositiveInfinity;
		}
		return sum;
	}

	// Whether a Star track's share along this axis waits on its Auto tracks: the extent is finite, and it has both.
	private static bool WaitsForAuto(ReadOnlySpan<GridLength> defs, float available)
	{
		if (!float.IsFinite(available))
			return false;
		bool auto = false, star = false;
		foreach (var d in defs)
		{
			auto |= d.IsAuto;
			star |= d.IsStar;
		}
		return auto && star;
	}

	// Whether a child's run of tracks takes a Star track's share: it spans a Star track and no Auto one (a cell with an Auto
	// track in it stays unbounded, as it sizes that track).
	private static bool InStarShare(ReadOnlySpan<GridLength> defs, int start, int span)
	{
		var end = Math.Min(defs.Length, start + Math.Max(1, span));
		var star = false;
		for (var i = start; i < end; i++)
		{
			if (defs[i].IsAuto)
				return false;
			star |= defs[i].IsStar;
		}
		return star;
	}

	private static int ClampIndex(int value, int length) => Math.Clamp(value, 0, length - 1);

	private static float Sum(float[] a) { float s = 0f; foreach (var v in a) s += v; return s; }

	// Each track's offset from the grid's origin: the sum of the tracks before it.
	private static void Offsets(float[] sizes, ref float[] offsets)
	{
		if (offsets.Length != sizes.Length)
			offsets = new float[sizes.Length];
		float acc = 0f;
		for (var i = 0; i < sizes.Length; i++) { offsets[i] = acc; acc += sizes[i]; }
	}
}
