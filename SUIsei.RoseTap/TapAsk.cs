using RoseMcp.Tap.Protocol;

namespace SUIsei.RoseTap;

/// <summary>
/// The toolbar's Ask button as a latch the host collects: the app never speaks first on the pipe, so a press waits here
/// until the host's next <c>ask</c>, and a later press replaces one not yet collected, since the latest press is the
/// question. Answered on the reader thread under a lock of its own, so asking costs the game's UI thread nothing.
/// <para>
/// The press count runs for the life of the process, so a press after a reconnect is never mistaken for one a host
/// already holds. It is kept in the process's <see cref="AppContext"/> data rather than in a static, which would start
/// again at zero whenever a rebuilt tap replaces this one in a running game.
/// </para>
/// </summary>
internal static class TapAsk
{
	private const string SequenceKey = "SUIsei.RoseTap.AskSequence";

	private static readonly Lock Gate = new();
	private static bool _pressed;
	private static long _collected;
	private static long _raisedAt;
	private static string _kind = "";
	private static string _readable = "";
	private static ulong _element;
	private static ulong _other;
	private static string _colour = "";

	/// <summary>Record a press, replacing any the host has not collected.</summary>
	public static void Raise(string kind, string readable, ulong element, ulong other, string colour)
	{
		lock (Gate)
		{
			var sequence = Sequence + 1;
			AppContext.SetData(SequenceKey, sequence);
			_pressed = true;
			_raisedAt = Environment.TickCount64;
			(_kind, _readable, _element, _other, _colour) = (kind, readable, element, other, colour);
		}
	}

	/// <summary>
	/// The latest press as an <c>ask</c> row, or <c>none</c>. <paramref name="seen"/> is the press the host already
	/// holds; <paramref name="arrived"/> says whether this read is the one that told the toolbar its press was
	/// collected, which is when the button stops showing a press waiting.
	/// </summary>
	public static string Reply(long seen, out bool arrived)
	{
		lock (Gate)
		{
			arrived = false;
			if (!_pressed)
				return "none";
			var sequence = Sequence;
			if (seen >= sequence && _collected < sequence)
			{
				_collected = sequence;
				arrived = true;
			}
			return TapRows.Ask(sequence, _kind, _readable, _element, _other, _colour, Environment.TickCount64 - _raisedAt);
		}
	}

	/// <summary>Whether a press is waiting for the host to collect it.</summary>
	public static bool Waiting
	{
		get
		{
			lock (Gate)
				return _pressed && _collected < Sequence;
		}
	}

	/// <summary>Forget the press, as a session ends: a later host starts from nothing. The count is kept, so the next
	/// press is numbered after every one before it.</summary>
	public static void Forget()
	{
		lock (Gate)
		{
			_pressed = false;
			_collected = Sequence;
			(_kind, _readable, _element, _other, _colour, _raisedAt) = ("", "", 0, 0, "", 0);
		}
	}

	private static long Sequence => AppContext.GetData(SequenceKey) is long sequence ? sequence : 0;
}
