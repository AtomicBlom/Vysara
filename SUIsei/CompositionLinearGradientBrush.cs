using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// Paints a gradient along the line from <see cref="StartPoint"/> to <see cref="EndPoint"/>, as WinUI's
/// <c>CompositionLinearGradientBrush</c>: the colour is constant across the line, and stop offsets measure along it.
/// </summary>
public sealed class CompositionLinearGradientBrush : CompositionGradientBrush
{
	/// <summary>Where offset 0 lies, in <see cref="CompositionGradientBrush.MappingMode"/> units.</summary>
	public Vector2 StartPoint { get; set => Change(ref field, value); }

	/// <summary>Where offset 1 lies, in <see cref="CompositionGradientBrush.MappingMode"/> units.</summary>
	public Vector2 EndPoint { get; set => Change(ref field, value); } = new(1f, 0f);

	/// <summary>The start and end in the local space of a box of <paramref name="size"/> at the origin.</summary>
	internal void Map(Vector2 size, out Vector2 start, out Vector2 end)
	{
		if (MappingMode == CompositionMappingMode.Relative)
		{
			start = StartPoint * size;
			end = EndPoint * size;
		}
		else
		{
			start = StartPoint;
			end = EndPoint;
		}
	}

	internal override void Describe(StringBuilder sb)
	{
		sb.Append(CultureInfo.InvariantCulture, $"linear(({StartPoint.X:0.###},{StartPoint.Y:0.###})->({EndPoint.X:0.###},{EndPoint.Y:0.###})");
		DescribeStops(sb);
		sb.Append(')');
	}
}
