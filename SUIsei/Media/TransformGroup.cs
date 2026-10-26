using System.Numerics;

namespace SUIsei.Media;

/// <summary>
/// Transforms applied one after another, in the order <see cref="Children"/> lists them, as WinUI's
/// <c>TransformGroup</c>. A change to any child, or to the list, moves every element showing the group.
/// </summary>
[ContentProperty(Name = nameof(Children))]
public sealed class TransformGroup : Transform, ISubPropertyListener
{
	public TransformGroup() => Children = new TransformCollection(this);

	/// <summary>The transforms, first applied first.</summary>
	public TransformCollection Children { get; }

	/// <summary>The combined transform as a matrix.</summary>
	public Matrix3x2 Value => ToMatrix();

	internal override Matrix3x2 ToMatrix()
	{
		var m = Matrix3x2.Identity;
		var children = Children.Items;
		for (var i = 0; i < children.Count; i++)
			m *= children[i].ToMatrix();
		return m;
	}

	/// <summary>The list or a child changed.</summary>
	internal void ChildrenChanged() => Changed();

	void ISubPropertyListener.OnSubPropertyChanged(object source) => Changed();
}
