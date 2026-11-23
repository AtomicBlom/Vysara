namespace Vysara.Animation;

/// <summary>A key that sets its property to <see cref="ObjectKeyFrame.Value"/> at <see cref="ObjectKeyFrame.KeyTime"/>,
/// holding the value before it until then, as WinUI's <c>DiscreteObjectKeyFrame</c>.</summary>
public sealed class DiscreteObjectKeyFrame : ObjectKeyFrame
{
}
