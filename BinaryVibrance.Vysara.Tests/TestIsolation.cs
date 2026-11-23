namespace BinaryVibrance.Vysara.Tests;

/// <summary>Keys for tests that must not run at the same time as one another.</summary>
internal static class TestIsolation
{
	/// <summary>
	/// The hot-reload tests switch on <see cref="HotReload"/>'s process-global registry, and every <see cref="UIHost.Tick"/>
	/// in the process then drains its pending rebuilds, whichever test queued them. A test that counts the bytes its own
	/// thread allocates across ticks must not overlap them, or it may count another test's rebuild.
	/// </summary>
	public const string HotReload = nameof(HotReload);
}
