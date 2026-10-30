using System.Numerics;
using SUIsei.Composition;
using SUIsei.Extensions;
using SUIsei.Media;

namespace SUIsei.Shapes;

/// <summary>
/// The base of the vector shapes, as WinUI's <c>Shape</c>: an outline filled by <see cref="Fill"/> and stroked by
/// <see cref="Stroke"/> with WinUI's stroke set (thickness, dashes, joins, miter limit and caps), sized by
/// <see cref="Stretch"/>. Every property is a dependency property, so a shape is stylable and its numbers and colours
/// animatable.
/// <para>
/// A shape draws through one retained <see cref="ShapeContent"/>, which it updates in place when a property changes and
/// never per frame. The tessellation is cached there, so moving, fading or recolouring a shape (its render transform,
/// its opacity, its brushes' colours) costs no layout and no tessellation. While it is in a live tree a shape listens to
/// its brushes, its dash array and its outline's geometry or points, as any element listens to its brushes.
/// </para>
/// <para>
/// <b>Layout.</b> With <see cref="Stretch.None"/> the outline is drawn in the shape's own coordinates, and the shape
/// asks for the space from its origin to the outline's far edges, the pen included when there is a stroke. Otherwise
/// the outline is scaled into the space the shape is given, half the pen kept inside each edge: <see cref="Stretch.Fill"/>
/// in each direction on its own, <see cref="Stretch.Uniform"/> by the smaller scale and <see cref="Stretch.UniformToFill"/>
/// by the larger, keeping its proportions. An outline that does not fill the space is centred in it. The pen is never
/// scaled. A <see cref="Rectangle"/> and an <see cref="Ellipse"/> are sized by the space itself instead.
/// </para>
/// <para>
/// <b>Hit-testing</b> follows what is drawn: a point hits inside the fill when <see cref="Fill"/> is set, and on the
/// stroke, within half its thickness of the outline along the dashes, caps and joins it draws, when <see cref="Stroke"/>
/// is. A shape with neither is not hit.
/// </para>
/// </summary>
public abstract class Shape : FrameworkElement
{
	/// <summary>The brush filling the outline; null fills nothing.</summary>
	public static readonly DependencyProperty<Shape, Brush?> FillProperty =
		DependencyProperty.Register(nameof(Fill),
			new PropertyMetadata<Shape, Brush?> { PropertyChanged = static (s, old, @new) => s.OnBrushChanged(old, @new) });

	/// <summary>The brush stroking the outline; null strokes nothing.</summary>
	public static readonly DependencyProperty<Shape, Brush?> StrokeProperty =
		DependencyProperty.Register(nameof(Stroke),
			new PropertyMetadata<Shape, Brush?>
			{
				PropertyChanged = static (s, old, @new) =>
				{
					s.OnBrushChanged(old, @new);
					if (old is null != @new is null)
						s.InvalidateMeasure(); // the pen counts in a shape's layout only while there is a stroke
				},
			});

	/// <summary>The stroke's width, centred on the outline. 1 by default.</summary>
	public static readonly DependencyProperty<Shape, float> StrokeThicknessProperty =
		DependencyProperty.Register(nameof(StrokeThickness),
			new PropertyMetadata<Shape, float> { DefaultValue = 1f, Invalidates = InvalidationFlags.Measure });

	/// <summary>The dash pattern, alternating dash and gap lengths in thicknesses.</summary>
	public static readonly DependencyProperty<Shape, DoubleCollection?> StrokeDashArrayProperty =
		DependencyProperty.Register(nameof(StrokeDashArray),
			new PropertyMetadata<Shape, DoubleCollection?> { PropertyChanged = static (s, old, @new) => s.OnDashArrayChanged(old, @new) });

	/// <summary>How far into the dash pattern the stroke starts, in thicknesses.</summary>
	public static readonly DependencyProperty<Shape, float> StrokeDashOffsetProperty = RegisterStyle<float>(nameof(StrokeDashOffset));

	/// <summary>The cap at both ends of each dash.</summary>
	public static readonly DependencyProperty<Shape, PenLineCap> StrokeDashCapProperty = RegisterStyle<PenLineCap>(nameof(StrokeDashCap));

	/// <summary>How the stroke turns corners.</summary>
	public static readonly DependencyProperty<Shape, PenLineJoin> StrokeLineJoinProperty = RegisterStyle<PenLineJoin>(nameof(StrokeLineJoin));

	/// <summary>How far a miter may reach from its corner, in half thicknesses.</summary>
	public static readonly DependencyProperty<Shape, float> StrokeMiterLimitProperty = RegisterStyle(nameof(StrokeMiterLimit), 10f);

	/// <summary>The cap at the start of an open outline.</summary>
	public static readonly DependencyProperty<Shape, PenLineCap> StrokeStartLineCapProperty = RegisterStyle<PenLineCap>(nameof(StrokeStartLineCap));

	/// <summary>The cap at the end of an open outline.</summary>
	public static readonly DependencyProperty<Shape, PenLineCap> StrokeEndLineCapProperty = RegisterStyle<PenLineCap>(nameof(StrokeEndLineCap));

	/// <summary>How the outline is sized into the space the shape is given.</summary>
	public static readonly DependencyProperty<Shape, Stretch> StretchProperty =
		DependencyProperty.Register(nameof(Stretch),
			new PropertyMetadata<Shape, Stretch> { DefaultValueFor = static s => s.DefaultStretch, Invalidates = InvalidationFlags.Measure });

	private readonly ShapeContent _content = new();
	private float[] _dashes = [];

	// Maps the outline into the shape's space under Stretch; identity with Stretch.None.
	private Matrix3x2 _stretch = Matrix3x2.Identity;

	private protected Shape()
	{
	}

	/// <summary>The brush filling the outline; null, the default, fills nothing. A <c>Polyline</c>'s open outline fills
	/// as if closed.</summary>
	public Brush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

	/// <summary>The brush stroking the outline; null, the default, strokes nothing.</summary>
	public Brush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

	/// <summary>The stroke's width, centred on the outline. 1 by default; 0 or less strokes nothing.</summary>
	public float StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

	/// <summary>
	/// The dash pattern, alternating dash and gap lengths in multiples of <see cref="StrokeThickness"/>, starting with a
	/// dash; an odd count repeats once to make it even. Empty, the default, strokes solid. A change to the collection
	/// applies in place. In XAML, <c>StrokeDashArray="2 1"</c>.
	/// </summary>
	public DoubleCollection StrokeDashArray
	{
		get
		{
			if (GetValue(StrokeDashArrayProperty) is { } dashes)
				return dashes;
			dashes = [];
			SetValue(StrokeDashArrayProperty, dashes);
			return dashes;
		}
		set => SetValue(StrokeDashArrayProperty, value);
	}

	/// <summary>How far into the dash pattern the stroke starts, in multiples of <see cref="StrokeThickness"/>.</summary>
	public float StrokeDashOffset { get => GetValue(StrokeDashOffsetProperty); set => SetValue(StrokeDashOffsetProperty, value); }

	/// <summary>The cap at both ends of each dash, where it does not end at the outline's start or end.
	/// <see cref="PenLineCap.Flat"/> by default.</summary>
	public PenLineCap StrokeDashCap { get => GetValue(StrokeDashCapProperty); set => SetValue(StrokeDashCapProperty, value); }

	/// <summary>How the stroke turns corners. <see cref="PenLineJoin.Miter"/> by default.</summary>
	public PenLineJoin StrokeLineJoin { get => GetValue(StrokeLineJoinProperty); set => SetValue(StrokeLineJoinProperty, value); }

	/// <summary>How far a miter may reach from its corner, in half thicknesses, before it is cut off there. 10 by
	/// default.</summary>
	public float StrokeMiterLimit { get => GetValue(StrokeMiterLimitProperty); set => SetValue(StrokeMiterLimitProperty, value); }

	/// <summary>The cap at the start of an open outline. <see cref="PenLineCap.Flat"/> by default.</summary>
	public PenLineCap StrokeStartLineCap { get => GetValue(StrokeStartLineCapProperty); set => SetValue(StrokeStartLineCapProperty, value); }

	/// <summary>The cap at the end of an open outline. <see cref="PenLineCap.Flat"/> by default.</summary>
	public PenLineCap StrokeEndLineCap { get => GetValue(StrokeEndLineCapProperty); set => SetValue(StrokeEndLineCapProperty, value); }

	/// <summary>How the outline is sized into the space the shape is given: <see cref="Stretch.Fill"/> by default for a
	/// <see cref="Rectangle"/> and an <see cref="Ellipse"/>, <see cref="Stretch.None"/> for the others, as in WinUI.</summary>
	public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }

	/// <summary>The <see cref="Stretch"/> this kind of shape has unless it is set.</summary>
	private protected virtual Stretch DefaultStretch => Stretch.None;

	/// <summary>The outline in the shape's own coordinates, before it is stretched; null or empty draws nothing.</summary>
	private protected abstract CompositionPath? DefiningPath { get; }

	/// <summary>A transform the outline's points go through before it is stretched: a path's geometry transform.</summary>
	private protected virtual Matrix3x2 DefiningTransform => Matrix3x2.Identity;

	/// <summary>The stroke's thickness where layout counts it: while there is a stroke, else 0, as in WinUI.</summary>
	private protected float PenThickness
	{
		get
		{
			var thickness = StrokeThickness;
			return Stroke is not null && thickness > 0f && float.IsFinite(thickness) ? thickness : 0f;
		}
	}

	protected override Size MeasureOverride(Size available)
	{
		if (DefiningPath is not { IsEmpty: false } path)
			return Size.Zero;
		var bounds = path.GetBounds(DefiningTransform);
		var pen = PenThickness;
		if (Stretch == Stretch.None)
			return new Size(MathF.Max(bounds.Right + pen / 2f, 0f), MathF.Max(bounds.Bottom + pen / 2f, 0f));
		return StretchInto(Stretch, pen, available, bounds, out _);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_stretch = Matrix3x2.Identity;
		if (Stretch != Stretch.None && DefiningPath is { IsEmpty: false } path)
		{
			var bounds = path.GetBounds(DefiningTransform);
			var pen = PenThickness;
			var size = StretchInto(Stretch, pen, finalSize, bounds, out var scale);
			// The stretched outline's corner sits half a pen in from the corner of what it fills, which is centred in the
			// space when it does not fill it.
			var leftover = new Vector2(MathF.Max(0f, finalSize.Width - size.Width), MathF.Max(0f, finalSize.Height - size.Height));
			_stretch = Matrix3x2.CreateTranslation(-bounds.X, -bounds.Y) * Matrix3x2.CreateScale(scale)
				* Matrix3x2.CreateTranslation(new Vector2(pen / 2f) + leftover / 2f);
		}
		return finalSize;
	}

	/// <summary>
	/// What a shape sized by its space (a <see cref="Rectangle"/>, an <see cref="Ellipse"/>) asks for: room for its pen,
	/// or with <see cref="Stretch.UniformToFill"/> a square as large as the space allows, the smaller side when one side
	/// is unlimited.
	/// </summary>
	private protected Size MeasureBox(Size available)
	{
		var pen = PenThickness;
		if (Stretch != Stretch.UniformToFill || (float.IsInfinity(available.Width) && float.IsInfinity(available.Height)))
			return new Size(pen, pen);
		var side = float.IsInfinity(available.Width) || float.IsInfinity(available.Height)
			? MathF.Min(available.Width, available.Height)
			: MathF.Max(available.Width, available.Height);
		return new Size(side, side);
	}

	/// <summary>
	/// The box a shape sized by its space draws in, given a space of <paramref name="size"/>: the space less half the
	/// pen inside each edge, square by its smaller side with <see cref="Stretch.Uniform"/> or its larger with
	/// <see cref="Stretch.UniformToFill"/>, from the top left; empty with <see cref="Stretch.None"/>.
	/// </summary>
	private protected Rect BoxIn(Size size)
	{
		var pen = PenThickness;
		var w = MathF.Max(0f, size.Width - pen);
		var h = MathF.Max(0f, size.Height - pen);
		switch (Stretch)
		{
			case Stretch.None:
				w = h = 0f;
				break;
			case Stretch.Uniform:
				w = h = MathF.Min(w, h);
				break;
			case Stretch.UniformToFill:
				w = h = MathF.Max(w, h);
				break;
		}
		return new Rect(pen / 2f, pen / 2f, w, h);
	}

	/// <summary>
	/// Scale an outline of <paramref name="bounds"/> into <paramref name="space"/>, a pen of <paramref name="pen"/> kept
	/// inside it, by <paramref name="mode"/>: the scale per axis, and the size the stretched outline and its pen then take.
	/// An axis the outline has no extent along (a horizontal line's height), or with no limit to fill (an infinite
	/// available size), keeps scale 1, and a flat outline is not kept in proportion.
	/// </summary>
	private protected static Size StretchInto(Stretch mode, float pen, Size space, in Rect bounds, out Vector2 scale)
	{
		var sx = MathF.Max(space.Width - pen, 0f);
		var sy = MathF.Max(space.Height - pen, 0f);
		var flat = false;
		if (bounds.Width > 0f && float.IsFinite(sx))
			sx /= bounds.Width;
		else
		{
			flat |= !(bounds.Width > 0f);
			sx = 1f;
		}
		if (bounds.Height > 0f && float.IsFinite(sy))
			sy /= bounds.Height;
		else
		{
			flat |= !(bounds.Height > 0f);
			sy = 1f;
		}
		if (mode != Stretch.Fill && !flat)
			sx = sy = mode == Stretch.Uniform ? MathF.Min(sx, sy) : MathF.Max(sx, sy);
		scale = new Vector2(sx, sy);
		return new Size(bounds.Width * sx + pen, bounds.Height * sy + pen);
	}

	private protected override VisualContent? UpdateContent()
	{
		var path = DefiningPath;
		if (path is not { IsEmpty: false } || (Fill is null && Stroke is null))
			return null;
		var content = _content;
		content.Path = path;
		content.PathTransform = DefiningTransform * _stretch;
		content.Fill = Fill?.CompositionBrush;
		content.Stroke = Stroke?.CompositionBrush;
		var style = content.StrokeStyle;
		style.Thickness = StrokeThickness;
		style.LineJoin = StrokeLineJoin;
		style.MiterLimit = StrokeMiterLimit;
		style.StartCap = StrokeStartLineCap;
		style.EndCap = StrokeEndLineCap;
		style.DashCap = StrokeDashCap;
		style.DashOffset = StrokeDashOffset;
		var dashes = GetValue(StrokeDashArrayProperty);
		var count = dashes?.Count ?? 0;
		if (_dashes.Length < count)
			_dashes = new float[count];
		for (var i = 0; i < count; i++)
			_dashes[i] = (float)dashes![i];
		style.SetDashArray(_dashes.AsSpan(0, count));
		content.Trim = CurrentTrim;
		content.StrokeBrushAlongLength = StrokeBrushMapping.GetMode(this) == StrokeBrushMappingMode.AlongLength;
		return content;
	}

	// The part of the stroke suisei:StrokeTrim leaves.
	private PathTrim CurrentTrim => new(StrokeTrim.GetStart(this), StrokeTrim.GetEnd(this), StrokeTrim.GetOffset(this));

	/// <summary>A <c>suisei:StrokeTrim</c> value changed: trim the drawn stroke in place, with no layout and no
	/// tessellation.</summary>
	internal void ApplyTrim()
	{
		_content.Trim = CurrentTrim;
		Visual.MarkDirty();
	}

	/// <summary><c>suisei:StrokeBrushMapping.Mode</c> changed: repaint the stroke in place.</summary>
	internal void OnStrokeMappingChanged() => InvalidateContent();

	/// <summary>The outline changed in a way that can move its bounds: lay the shape out again, which redraws it.</summary>
	private protected void OnOutlineChanged() => InvalidateMeasure();

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Fill, attached);
		Listen(Stroke, attached);
		ListenToDashes(GetValue(StrokeDashArrayProperty), attached);
		base.OnLiveTreeChanged(attached);
		// The outline, its points or the dash array may have changed while the shape was away and not listening: lay it
		// out again, which redraws it, since its last measure is no longer to be trusted.
		if (attached)
			InvalidateMeasure();
	}

	private protected override void OnSubPropertyChanged(object source)
	{
		if (ReferenceEquals(source, GetValue(StrokeDashArrayProperty)))
			InvalidateContent();
		else
			base.OnSubPropertyChanged(source);
	}

	private void OnDashArrayChanged(DoubleCollection? oldValue, DoubleCollection? newValue)
	{
		if (AttachedRoot is not null)
		{
			ListenToDashes(oldValue, false);
			ListenToDashes(newValue, true);
		}
		InvalidateContent();
	}

	private void ListenToDashes(DoubleCollection? dashes, bool listen)
	{
		if (dashes is null)
			return;
		if (listen)
			dashes.AddListener(this);
		else
			dashes.RemoveListener(this);
	}

	// A property of the stroke's style: a change redraws the shape in place, with no layout.
	private static DependencyProperty<Shape, T> RegisterStyle<T>(string name, T defaultValue = default!) =>
		DependencyProperty.Register(name,
			new PropertyMetadata<Shape, T> { DefaultValue = defaultValue, PropertyChanged = static (s, _, _) => s.InvalidateContent() });
}
