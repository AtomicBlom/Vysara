using System.Globalization;
using System.Numerics;
using System.Text;

namespace Vysara.Composition;

/// <summary>
/// A retained composition node — the tree a pluggable <see cref="IUICompositor"/> renders.
/// Carries a 2D <see cref="Transform"/> (animatable independently of layout), group <see cref="Opacity"/>, an
/// optional <see cref="Clip"/>, and a damage flag for incremental recomposition. Backend-neutral.
/// </summary>
public abstract class Visual
{
	private readonly List<Visual> _children = [];

	// Every property setter below marks the visual dirty when the value changes, so a write from anywhere (layout, an
	// animation, an element's Translation) reaches the next composite.

	/// <summary>Affine transform relative to the parent visual: layout's offset, with the element's render transform and
	/// translation composed in.</summary>
	public Matrix3x2 Transform { get; set => Change(ref field, value); } = Matrix3x2.Identity;

	/// <summary>Group opacity (multiplies down the subtree).</summary>
	public float Opacity { get; set => Change(ref field, value); } = 1f;

	/// <summary>Optional clip rectangle in this visual's local space.</summary>
	public Rect? Clip { get; set => Change(ref field, value); }

	/// <summary>
	/// Optional post effect (e.g. a blur) applied to this visual's subtree as a group. A non-null effect forces the
	/// subtree to composite as one render-to-texture layer, the same path a sub-1 <see cref="Opacity"/> on a
	/// container takes.
	/// </summary>
	public LayerEffect? Effect { get; set => Change(ref field, value); }

	/// <summary>Damage flag — a visual-only change marks the subtree for recomposition (the incremental path). Only
	/// <see cref="DrawListCompositor"/> clears it, as it composites the visual; another compositor reads
	/// <see cref="Version"/> instead.</summary>
	public bool IsDirty { get; internal set; } = true;

	/// <summary>
	/// Moves on every change that marks this visual dirty: a property set to a different value, a child added or the
	/// children cleared, different content, or <see cref="MarkDirty"/> from whoever changed the content in place or a
	/// paint it shows. Never 0, so a compositor can use 0 for "not seen yet", and never cleared, so each compositor
	/// keeps the last version it composited and compares. A change to a child moves the child's version, not this one's.
	/// <para>
	/// The root visual's version moving asks for the whole tree to be composited again, whatever the root's own properties
	/// did. A <see cref="UIHost"/> marks the root, and only the root, when a <see cref="Drawing.TextureSource"/> the tree
	/// draws is disposed, which the tree's next list must leave out; <see cref="DrawListCompositor"/> marks it after a
	/// composite of the tree throws. A compositor that keeps output per visual treats the root's version moving as every
	/// visual's having moved.
	/// </para>
	/// </summary>
	public uint Version { get; private set; } = 1;

	public IReadOnlyList<Visual> Children => _children;

	public void Add(Visual child) { _children.Add(child); MarkDirty(); }
	public void Clear() { _children.Clear(); MarkDirty(); }

	/// <summary>Mark this visual (and implicitly its subtree, via the walk) as needing recomposition, and move its
	/// <see cref="Version"/>.</summary>
	public void MarkDirty()
	{
		IsDirty = true;
		if (++Version == 0)
			Version = 1;
	}

	/// <summary>Store <paramref name="value"/> in <paramref name="slot"/>, marking the visual dirty if it differs.</summary>
	private protected void Change<T>(ref T slot, T value)
	{
		if (EqualityComparer<T>.Default.Equals(slot, value))
			return;
		slot = value;
		MarkDirty();
	}

	/// <summary>A deterministic text form of the composition subtree — the element→visual test seam.</summary>
	public string Dump()
	{
		var sb = new StringBuilder();
		Dump(sb, 0);
		return sb.ToString();
	}

	private protected virtual void DescribeSelf(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(c, $"{GetType().Name} xform=[{Transform.M31:0.###},{Transform.M32:0.###}] op={Opacity:0.###}");
		if (Clip is { } cl) sb.Append(c, $" clip={cl}");
		if (Effect is { } fx) sb.Append(c, $" fx={fx.Id}");
	}

	private void Dump(StringBuilder sb, int depth)
	{
		sb.Append(' ', depth * 2);
		DescribeSelf(sb);
		sb.Append('\n');
		foreach (var c in _children)
			c.Dump(sb, depth + 1);
	}
}
