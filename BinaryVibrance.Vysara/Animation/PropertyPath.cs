namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// What a storyboard animation drives on its target: a dependency property of the target, or one reached through
/// properties whose values are objects, as in WinUI's <c>(UIElement.RenderTransform).(CompositeTransform.TranslateX)</c>
/// or <c>(Border.Background).(SolidColorBrush.Color)</c>. The XAML generator resolves a <c>Storyboard.TargetProperty</c>
/// path to one of these at compile time; the steps are walked when the animation begins, and a step may name a more
/// derived type than the property before it holds (a <c>SolidColorBrush</c> property of a <c>Brush</c>), as long as the
/// object found is one.
/// </summary>
public sealed class PropertyPath
{
	private readonly DependencyProperty[] _steps;

	/// <summary>A path through <paramref name="steps"/>: each property but the last holds the object the next is read
	/// from, and the last is the animated property.</summary>
	public PropertyPath(params DependencyProperty[] steps)
	{
		ArgumentOutOfRangeException.ThrowIfZero(steps.Length);
		_steps = steps;
	}

	public static implicit operator PropertyPath(DependencyProperty property) => new(property);

	/// <summary>Walk the path from <paramref name="target"/> to the object that holds the animated property, and that
	/// property. Throws when a step holds no object, holds one the next step does not apply to, or, for an animation that
	/// <paramref name="interpolates"/>, the last property is not animatable. A discrete animation sets any property.</summary>
	internal (DependencyObject Owner, IAnimationTarget Target) Resolve(DependencyObject target, bool interpolates)
	{
		var owner = target;
		for (var i = 0; i < _steps.Length - 1; i++)
		{
			owner = _steps[i].GetObjectValue(owner)
				?? throw new InvalidOperationException($"Property path {this}: {_steps[i].Name} holds no object to step into.");
			if (!_steps[i + 1].AppliesTo(owner))
				throw new InvalidOperationException(
					$"Property path {this}: {_steps[i].Name} holds a {owner.GetType().Name}, which has no {_steps[i + 1].Name}.");
		}

		var last = _steps[^1];
		if (!last.AppliesTo(owner))
			throw new InvalidOperationException($"Property path {this}: a {owner.GetType().Name} has no {last.Name}.");
		if (interpolates && !last.IsAnimatable)
			throw new InvalidOperationException($"Property path {this}: {last.Name} is not animatable; its type has no interpolation.");
		return (owner, (IAnimationTarget)last);
	}

	public override string ToString() => string.Join(".", _steps.Select(static s => $"({s.Name})"));
}
