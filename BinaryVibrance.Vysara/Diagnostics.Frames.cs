namespace BinaryVibrance.Vysara;

public static partial class Diagnostics
{
	/// <summary>
	/// Engine glue that reads back the frame the game presented, so a diagnostics magnifier sees the scene behind the UI
	/// as well as the UI. Vysara never draws to the screen itself, so only the engine can supply this.
	/// </summary>
	public interface IFrameSource
	{
		/// <summary>
		/// Copy a rectangle of the most recently presented frame into <paramref name="rgba8"/>: <paramref name="width"/>
		/// by <paramref name="height"/> pixels from (<paramref name="x"/>, <paramref name="y"/>) in the frame's pixels,
		/// four bytes each (red, green, blue, alpha, sRGB-encoded), top row first and tightly packed. Pixels outside the
		/// frame are transparent black. The frame may be a frame or two old. False, leaving
		/// <paramref name="rgba8"/> as it was, when no frame has been read back yet.
		/// </summary>
		bool TryReadPixels(int x, int y, int width, int height, Span<byte> rgba8);
	}

	/// <summary>The frame source engine glue supplied with <see cref="SetFrameSource"/>, or null when it supplied
	/// none.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static IFrameSource? FrameSource
	{
		get
		{
			RequireEnabled();
			return Volatile.Read(ref _frameSource);
		}
	}

	private static IFrameSource? _frameSource;

	/// <summary>Supply the presented frame's pixels to diagnostics tools, or withdraw them with null. Engine glue calls
	/// this once its renderer is up; it does nothing while diagnostics are off, so glue that wants the source trimmed
	/// with them builds it inside <c>if (Diagnostics.Enabled)</c>.</summary>
	public static void SetFrameSource(IFrameSource? source)
	{
		if (Enabled)
			Volatile.Write(ref _frameSource, source);
	}
}
