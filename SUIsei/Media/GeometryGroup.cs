using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// Several geometries drawn as one, as WinUI's <c>GeometryGroup</c>: their figures together, each through its own
/// <see cref="Geometry.Transform"/>, filled by the group's own <see cref="FillRule"/> (the children's fill rules are not
/// used), so overlapping children cut holes under <see cref="FillRule.EvenOdd"/> and merge under
/// <see cref="FillRule.Nonzero"/>. The group's own transform applies on top, where the group is used.
/// </summary>
[ContentProperty(Name = nameof(Children))]
public sealed class GeometryGroup : Geometry
{
	public static readonly DependencyProperty<GeometryGroup, GeometryCollection?> ChildrenProperty =
		DependencyProperty.Register(nameof(Children), new PropertyMetadata<GeometryGroup, GeometryCollection?>
		{
			// Checked before the group listens to them: children that hold the group, directly or through a group inside
			// them, would put it inside itself, and building or notifying it would never end.
			Validate = static (group, children) =>
			{
				if (children is { Count: > 0 } && GeometryCollection.Reaches(children, group, null, []))
					throw new ArgumentException("A GeometryGroup can't hold itself, directly or through a group inside it.", nameof(Children));
			},
			PropertyChanged = static (group, oldValue, newValue) =>
			{
				oldValue?.RemoveListener(group);
				newValue?.AddListener(group);
				group.Changed();
			},
		});

	public static readonly DependencyProperty<GeometryGroup, FillRule> FillRuleProperty =
		DependencyProperty.Register(nameof(FillRule), new PropertyMetadata<GeometryGroup, FillRule> { PropertyChanged = OnChanged });

	/// <summary>The geometries drawn together.</summary>
	public GeometryCollection Children
	{
		get
		{
			if (GetValue(ChildrenProperty) is { } children)
				return children;
			children = [];
			SetValue(ChildrenProperty, children);
			return children;
		}
		set => SetValue(ChildrenProperty, value);
	}

	/// <summary>How the children's overlaps decide what is inside. <see cref="FillRule.EvenOdd"/> by default.</summary>
	public FillRule FillRule { get => GetValue(FillRuleProperty); set => SetValue(FillRuleProperty, value); }

	private protected override void BuildPath(CompositionPath path)
	{
		path.FillRule = FillRule;
		if (GetValue(ChildrenProperty) is not { } children)
			return;
		for (var i = 0; i < children.Count; i++)
			path.Append(children[i].CompositionPath, children[i].TransformMatrix);
	}
}
