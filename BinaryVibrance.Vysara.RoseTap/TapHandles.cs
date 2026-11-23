using System.Runtime.CompilerServices;

namespace BinaryVibrance.Vysara.RoseTap;

/// <summary>
/// The numbers the tap gives elements on the wire. An element keeps its number for as long as it lives, so a handle a
/// host read from one <c>tree</c> still names the same element in the next; a collected element simply stops
/// resolving, and its number is never given to another. Numbers count up from one, because zero means no element.
/// <para>
/// Both directions are weak: the element-to-number table is a <see cref="ConditionalWeakTable{TKey, TValue}"/> and the
/// way back holds weak references, so a handle table never keeps the game's UI alive.
/// </para>
/// </summary>
internal sealed class TapHandles
{
	private readonly Lock _gate = new();
	private readonly ConditionalWeakTable<UIElement, StrongBox<ulong>> _numbers = new();
	private readonly Dictionary<ulong, WeakReference<UIElement>> _elements = [];
	private ulong _last;

	/// <summary>The number <paramref name="element"/> goes by, given now if it has none yet.</summary>
	public ulong HandleOf(UIElement element)
	{
		lock (_gate)
		{
			if (_numbers.TryGetValue(element, out var known))
				return known.Value;
			var handle = ++_last;
			_numbers.Add(element, new StrongBox<ulong>(handle));
			_elements[handle] = new WeakReference<UIElement>(element);
			return handle;
		}
	}

	/// <summary>The element <paramref name="handle"/> names, or null for zero, a number never given, or an element that
	/// has been collected.</summary>
	public UIElement? Resolve(ulong handle)
	{
		lock (_gate)
			return _elements.TryGetValue(handle, out var weak) && weak.TryGetTarget(out var element) ? element : null;
	}

	/// <summary>Forget the numbers of elements that have been collected.</summary>
	public void Prune()
	{
		lock (_gate)
		{
			foreach (var (handle, weak) in _elements)
			{
				if (!weak.TryGetTarget(out _))
					_elements.Remove(handle);
			}
		}
	}

	/// <summary>Forget every number, as a session ends.</summary>
	public void Clear()
	{
		lock (_gate)
		{
			_numbers.Clear();
			_elements.Clear();
		}
	}
}
