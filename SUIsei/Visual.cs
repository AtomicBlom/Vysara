using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// A retained composition node (PlanningUI.md §D2.5) — the tree a pluggable <see cref="IUICompositor"/> renders.
/// Carries a 2D <see cref="Transform"/> (animatable independently of layout), group <see cref="Opacity"/>, an
/// optional <see cref="Clip"/>, and a damage flag for incremental recomposition. Backend-neutral.
/// </summary>
public abstract class Visual
{
	private readonly List<Visual> _children = [];

	/// <summary>Affine transform relative to the parent visual (layout writes the offset; animation may write more).</summary>
	public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

	/// <summary>Group opacity (multiplies down the subtree).</summary>
	public float Opacity { get; set; } = 1f;

	/// <summary>Optional clip rectangle in this visual's local space.</summary>
	public Rect? Clip { get; set; }

	/// <summary>Damage flag — a visual-only change marks the subtree for recomposition (MU2 incremental path).</summary>
	public bool IsDirty { get; internal set; } = true;

	public IReadOnlyList<Visual> Children => _children;

	public void Add(Visual child) { _children.Add(child); MarkDirty(); }
	public void Clear() { _children.Clear(); MarkDirty(); }

	/// <summary>Mark this visual (and implicitly its subtree, via the walk) as needing recomposition.</summary>
	public void MarkDirty() => IsDirty = true;

	/// <summary>A deterministic text form of the composition subtree — the element→visual test seam (PlanningUI.md §D8.1).</summary>
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
