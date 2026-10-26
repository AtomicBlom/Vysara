namespace SUIsei.Tests;

/// <summary>Counting the bytes a stretch of steady-state work allocates on the test's thread.</summary>
internal static class Allocations
{
	/// <summary>
	/// The bytes <paramref name="window"/> allocates on this thread, measured over a run with no garbage collection in it.
	/// Under a busy parallel suite another thread's allocations can trigger a gen-0 collection mid-window, which resets
	/// this thread's allocation context, and the count can then include part of an unused allocation quantum (a few KB)
	/// that nothing allocated. A window that ran through a collection is measured again, up to
	/// <paramref name="attempts"/> times; real allocations show in every window, so the smallest count is returned.
	/// </summary>
	public static long Measure(Action window, int attempts = 5)
	{
		var smallest = long.MaxValue;
		for (var i = 0; i < attempts; i++)
		{
			var collections = GC.CollectionCount(0);
			var before = GC.GetAllocatedBytesForCurrentThread();
			window();
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			smallest = Math.Min(smallest, allocated);
			if (GC.CollectionCount(0) == collections)
				return allocated;
		}
		return smallest;
	}
}
