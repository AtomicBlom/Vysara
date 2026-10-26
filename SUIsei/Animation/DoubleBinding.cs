namespace SUIsei.Animation;

/// <summary>
/// Binds a double animation to its target. Double animations author in double, as WinUI's do, while SUIsei's own
/// properties are float, so a double animation drives either, converting on write and read.
/// </summary>
internal static class DoubleBinding
{
	public static bool TryBind(AnimationTimeline<double> animation, DependencyObject owner, IAnimationTarget target)
	{
		if (target is not IAnimationTarget<float> single)
			return animation.BindDirect(owner, target);
		animation.Apply = value => single.Write(owner, (float)value);
		animation.Release = () => single.Release(owner);
		animation.ReadCurrent = () => single.Read(owner);
		return true;
	}
}
