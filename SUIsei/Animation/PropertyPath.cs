namespace SUIsei.Animation;

/// <summary>
/// What a storyboard animation drives on its target: a dependency property of the target, or one reached through
/// properties whose values are objects, as in WinUI's <c>(UIElement.RenderTransform).(CompositeTransform.TranslateX)</c>.
/// The XAML generator resolves a <c>Storyboard.TargetProperty</c> path to one of these at compile time; the steps are
/// walked when the animation begins.
/// </summary>
public sealed class PropertyPath
{
	private readonly DependencyProperty[] _steps;
	private readonly IAnimationTarget? _channel;

	/// <summary>A path through <paramref name="steps"/>: each property but the last holds the object the next is read
	/// from, and the last is the animated property.</summary>
	public PropertyPath(params DependencyProperty[] steps)
	{
		ArgumentOutOfRangeException.ThrowIfZero(steps.Length);
		_steps = steps;
	}

	private PropertyPath(IAnimationTarget channel)
	{
		_steps = [];
		_channel = channel;
	}

	/// <summary>
	/// An element's paint colour: <c>Background</c> on a Border, Panel or ContentControl, <c>Foreground</c> on a
	/// TextBlock. Those are plain colour properties rather than dependency properties, so they animate through the
	/// element's animated fill, which overrides the colour on the visual path with no relayout. Driven by a
	/// <see cref="ColorAnimation"/>.
	/// </summary>
	public static PropertyPath Fill { get; } = new(ElementChannels.Fill);

	/// <summary>The x offset an animation adds to an element's render transform. <c>RenderTransform</c> is a plain
	/// matrix rather than a transform object, so the offset is its own channel. Driven by a double animation.</summary>
	public static PropertyPath TranslateX { get; } = new(ElementChannels.TranslateX);

	/// <summary>The y offset an animation adds to an element's render transform; see <see cref="TranslateX"/>.</summary>
	public static PropertyPath TranslateY { get; } = new(ElementChannels.TranslateY);

	public static implicit operator PropertyPath(DependencyProperty property) => new(property);

	/// <summary>Walk the path from <paramref name="target"/> to the object that holds the animated property, and that
	/// property. Throws when a step holds no object or the last property is not animatable.</summary>
	internal (DependencyObject Owner, IAnimationTarget Target) Resolve(DependencyObject target)
	{
		if (_channel is not null)
			return (target, _channel);

		var owner = target;
		for (var i = 0; i < _steps.Length - 1; i++)
			owner = _steps[i].GetObjectValue(owner)
				?? throw new InvalidOperationException($"Property path {this}: {_steps[i].Name} holds no object to step into.");

		var last = _steps[^1];
		if (!last.IsAnimatable)
			throw new InvalidOperationException($"Property path {this}: {last.Name} is not animatable; its type has no interpolation.");
		return (owner, (IAnimationTarget)last);
	}

	public override string ToString() => _channel?.Name ?? string.Join(".", _steps.Select(static s => $"({s.Name})"));
}
