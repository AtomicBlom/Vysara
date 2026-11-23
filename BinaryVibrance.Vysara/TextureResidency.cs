using System.Globalization;
using System.Text;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara;

/// <summary>
/// The texture sources a <see cref="UIHost"/>'s surfaces draw, counted across them, and those its last render let go of.
/// A source is held exactly while the current draw list of one of the host's surfaces names it in its slot table, so one
/// reported in <see cref="Released"/> is named by no list the host is about to draw. A disposed source is found at the
/// start of the next render, and the surfaces that drew it are redrawn without it in that render, which therefore
/// releases it. <see cref="TextureSource.White"/> is never counted or reported: a host keeps it for its whole life.
/// <para>
/// Sources keep no reference to the hosts that hold them, so a long-lived source (the default font atlas, a cached image)
/// never keeps a host or its trees alive, and disposing a source on another thread touches no host state.
/// </para>
/// </summary>
internal sealed class TextureResidency(IReadOnlyList<UIView> views)
{
	// How many of the host's surfaces hold each source. A source whose count falls to 0 keeps its entry until the render
	// ends, so a surface taking it up later in the same render keeps it held rather than releasing and re-uploading it.
	private readonly Dictionary<TextureSource, int> _held = new(ReferenceEqualityComparer.Instance);
	private readonly List<TextureSource> _unheld = [];
	private readonly List<TextureSource> _disposed = [];
	private readonly List<TextureSource> _released = [];

	/// <summary>The sources the last render let go of, in the order it did: for the host to release.</summary>
	public IReadOnlyList<TextureSource> Released => _released;

	/// <summary>How many sources the host's surfaces hold.</summary>
	public int HeldCount => _held.Count;

	/// <summary>Start a render: redraw every surface that draws a source disposed since it was drawn, so this render's
	/// lists no longer name it and it is released at the end. One version check per held source; allocates
	/// nothing.</summary>
	public void BeginRender()
	{
		_disposed.Clear();
		foreach (var (source, _) in _held)
		{
			if (source.IsDisposed)
				_disposed.Add(source);
		}
		for (var d = 0; d < _disposed.Count; d++)
		{
			for (var i = 0; i < views.Count; i++)
			{
				var view = views[i];
				if (view.Textures.Holds(_disposed[d]))
					view.Root.Visual.MarkDirty();
			}
		}
	}

	/// <summary>Start holding <paramref name="source"/> for one more surface.</summary>
	public void Hold(TextureSource source)
	{
		_held[source] = _held.TryGetValue(source, out var count) ? count + 1 : 1;
	}

	/// <summary>Stop holding <paramref name="source"/> for one surface; when no surface holds it, it is released at the end
	/// of the render.</summary>
	public void Unhold(TextureSource source)
	{
		var count = _held[source];
		_held[source] = count - 1;
		if (count == 1)
			_unheld.Add(source);
	}

	/// <summary>End a render: publish the sources no surface's list names any more.</summary>
	public void EndRender()
	{
		_released.Clear();
		for (var i = 0; i < _unheld.Count; i++)
		{
			var source = _unheld[i];
			if (!_held.TryGetValue(source, out var count) || count > 0)
				continue; // taken up again by a later surface, or already published
			_held.Remove(source);
			_released.Add(source);
		}
		_unheld.Clear();
	}

	/// <summary>The held sources, each with how many surfaces hold it, and the last render's releases.</summary>
	public void Dump(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(c, $"Textures held ({_held.Count}):\n");
		foreach (var (source, count) in _held)
		{
			sb.Append("  ");
			source.Describe(sb);
			sb.Append(c, $" surfaces={count}\n");
		}
		sb.Append(c, $"Textures released ({_released.Count}):\n");
		foreach (var source in _released)
		{
			sb.Append("  ");
			source.Describe(sb);
			sb.Append('\n');
		}
	}
}

/// <summary>
/// The sources one surface's current draw list holds through a host's <see cref="TextureResidency"/>, updated when the
/// list's slot table may have changed.
/// </summary>
internal sealed class SurfaceTextures
{
	private HashSet<TextureSource> _held = new(ReferenceEqualityComparer.Instance);
	private HashSet<TextureSource> _next = new(ReferenceEqualityComparer.Instance);
	// The list last tracked, by revision: (0, 0) before the first, which no compositor's list carries.
	private (uint Revision, uint TextureRevision) _tracked;

	public bool Holds(TextureSource source) => _held.Contains(source);

	/// <summary>Hold the sources <paramref name="list"/> samples and let go of those it no longer does. Runs only when the
	/// list's revision or texture revision moved; allocates nothing once its sets have grown to the surface's sources. A
	/// disposed source the list still names is held like any other, until a redraw drops it.</summary>
	public void Track(DrawListData list, TextureResidency residency)
	{
		var revisions = (list.Revision, list.TextureRevision);
		if (revisions == _tracked && revisions != default)
			return;
		_tracked = revisions;

		var slots = list.TextureSlots;
		_next.Clear();
		for (var i = 0; i < slots.Count; i++)
		{
			var source = slots[i];
			if (ReferenceEquals(source, TextureSource.White))
				continue;
			if (_next.Add(source) && !_held.Contains(source))
				residency.Hold(source);
		}
		foreach (var source in _held)
		{
			if (!_next.Contains(source))
				residency.Unhold(source);
		}
		(_held, _next) = (_next, _held);
	}

	/// <summary>The surface is gone: let go of everything it held.</summary>
	public void Clear(TextureResidency residency)
	{
		foreach (var source in _held)
			residency.Unhold(source);
		_held.Clear();
		_tracked = default;
	}
}
