using System.Numerics;

namespace SUIsei.Tests;

/// <summary>Tolerant comparisons for geometry computed in floating point.</summary>
internal static class GeometryAssertions
{
	extension(Rect actual)
	{
		/// <summary>Each of the rect's edges is within <paramref name="tolerance"/> of <paramref name="expected"/>'s.</summary>
		public void ShouldBeNear(Rect expected, float tolerance)
		{
			var close = MathF.Abs(actual.X - expected.X) <= tolerance && MathF.Abs(actual.Y - expected.Y) <= tolerance
				&& MathF.Abs(actual.Right - expected.Right) <= tolerance && MathF.Abs(actual.Bottom - expected.Bottom) <= tolerance;
			close.ShouldBeTrue($"{actual} should be within {tolerance} of {expected}");
		}
	}

	extension(Vector2 actual)
	{
		public void ShouldBeNear(Vector2 expected, float tolerance) =>
			Vector2.Distance(actual, expected).ShouldBeLessThanOrEqualTo(tolerance, $"{actual} should be within {tolerance} of {expected}");
	}
}
