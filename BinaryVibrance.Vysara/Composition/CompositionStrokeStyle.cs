namespace BinaryVibrance.Vysara.Composition;

/// <summary>
/// How a path is stroked: thickness, joins, caps and dashes, as WinUI's composition shapes describe it
/// (<c>CompositionSpriteShape.StrokeThickness</c>, <c>StrokeLineJoin</c>, <c>StrokeMiterLimit</c>, <c>StrokeStartCap</c>,
/// <c>StrokeEndCap</c>, <c>StrokeDashCap</c>, <c>StrokeDashOffset</c> and <c>StrokeDashArray</c>), with the defaults of
/// XAML's <c>Shape</c>. Dash lengths and the dash offset are in multiples of <see cref="Thickness"/>, as in WinUI.
/// <para>
/// Retained and versioned like <see cref="CompositionPath"/>: every change advances <see cref="Version"/>, which a
/// tessellation is keyed by, and setting a value equal to the current one changes nothing.
/// </para>
/// </summary>
public sealed class CompositionStrokeStyle
{
	private readonly RetainedBuffer<float> _dashes = new(4);

	/// <summary>Advances on every change; never 0.</summary>
	public uint Version { get; private set; } = 1;

	/// <summary>The stroke's width, centred on the path. 1 by default; 0 or less draws nothing.</summary>
	public float Thickness { get; set => Change(ref field, value); } = 1f;

	/// <summary>How the stroke turns corners. <see cref="CompositionStrokeLineJoin.Miter"/> by default.</summary>
	public CompositionStrokeLineJoin LineJoin { get; set => Change(ref field, value); }

	/// <summary>The longest a miter may reach from its corner, in half thicknesses; a longer one is cut off there. 10 by
	/// default, and treated as at least 1.</summary>
	public float MiterLimit { get; set => Change(ref field, value); } = 10f;

	/// <summary>The cap at the start of each open figure. <see cref="CompositionStrokeCap.Flat"/> by default.</summary>
	public CompositionStrokeCap StartCap { get; set => Change(ref field, value); }

	/// <summary>The cap at the end of each open figure. <see cref="CompositionStrokeCap.Flat"/> by default.</summary>
	public CompositionStrokeCap EndCap { get; set => Change(ref field, value); }

	/// <summary>The cap at both ends of each dash, where a dash does not end at a figure's start or end.
	/// <see cref="CompositionStrokeCap.Flat"/> by default.</summary>
	public CompositionStrokeCap DashCap { get; set => Change(ref field, value); }

	/// <summary>How far into the dash pattern each figure starts, in thicknesses. 0 by default.</summary>
	public float DashOffset { get; set => Change(ref field, value); }

	/// <summary>
	/// The dash pattern, alternating dash and gap lengths in thicknesses, starting with a dash; an odd count repeats
	/// once to make it even. Empty, the default, draws a solid stroke, as does a pattern that sums to 0. The pattern
	/// restarts at each figure.
	/// </summary>
	public ReadOnlySpan<float> DashArray => _dashes.Span;

	/// <summary>Whether the stroke is dashed: its pattern sums to more than 0.</summary>
	public bool IsDashed
	{
		get
		{
			foreach (var d in _dashes.Span)
			{
				if (d > 0)
					return true;
			}
			return false;
		}
	}

	/// <summary>Replace the dash pattern, reusing its storage. An equal pattern changes nothing.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A length is negative or not finite.</exception>
	public void SetDashArray(ReadOnlySpan<float> dashes)
	{
		foreach (var d in dashes)
		{
			if (!float.IsFinite(d) || d < 0)
				throw new ArgumentOutOfRangeException(nameof(dashes), d, "Dash and gap lengths are finite and 0 or more.");
		}
		if (dashes.SequenceEqual(_dashes.Span))
			return;
		_dashes.Clear();
		foreach (var d in dashes)
			_dashes.Add(d);
		Version++;
	}

	private void Change<T>(ref T slot, T value)
	{
		if (EqualityComparer<T>.Default.Equals(slot, value))
			return;
		slot = value;
		Version++;
	}
}
