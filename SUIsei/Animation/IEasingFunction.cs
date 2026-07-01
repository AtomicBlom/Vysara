namespace SUIsei.Animation;

/// <summary>Maps linear progress <c>t</c> in <c>[0,1]</c> to an eased progress.</summary>
public interface IEasingFunction
{
	/// <summary>Ease the normalized progress <paramref name="t"/> (in <c>[0,1]</c>).</summary>
	double Ease(double t);
}
