using System.Numerics;
using System.Reflection;
using System.Text;

using RoseMcp.Tap.Protocol;

namespace SUIsei.RoseTap;

/// <summary>
/// What a person sees of the tap in their game: a toolbar on each view, picking, the hover and pick outlines, rulers,
/// the marks on what live edits changed, and the Ask button. The same modes and replies as the XAML taps' toolbar, built
/// from SUIsei's own elements as one diagnostics overlay per view.
/// <para>
/// Everything here runs on the UI thread. While installed it refreshes once a tick, by posting itself again, so the
/// marks follow elements the game moves, a pick whose element leaves the tree is cleared with the reason, and views the
/// game adds later get a toolbar too.
/// </para>
/// <para>
/// One pointer mode at a time. Picking and rulers both show the capture layer over the app and hold the host's
/// <see cref="UIHost.WantsPointer"/> true, so a game that withholds input while its UI wants the pointer never sees the
/// click that picks.
/// </para>
/// </summary>
internal sealed class TapOverlay
{
	private enum Pointing { None, Select, Rulers }

	// How many candidates a pick records: deep templates go on a long way, and the top of the stack is the useful part.
	private const int MaxCandidates = 16;

	// How long a change mark stays drawn, and how many elements and changes each it keeps.
	private static readonly TimeSpan MarkLifetime = TimeSpan.FromMinutes(10);
	private const int MaxMarkedElements = 24;
	private const int MaxChangesPerElement = 8;

	private const string Removed =
		"The selected element was removed from the tree, so the selection was cleared. Something in the game took it away; "
		+ "read the tree again and select what you want from it.";

	private readonly TapHandles _handles;
	private readonly TapLog _log;
	private readonly Dictionary<UIView, TapOverlayView> _views = [];
	private readonly Dictionary<ulong, Marked> _marked = [];
	private readonly Action _refresh;
	private readonly HashSet<UIHost> _held = [];

	private bool _installed;
	private Pointing _pointing;
	private bool _includeAll;
	private bool _justMyMarkup = true;
	private bool _changesShown = true;

	private WeakReference<UIElement>? _pick;
	private ulong _pickHandle;
	private string _rows = "";
	private string _gone = "";

	private WeakReference<UIElement>? _hover;

	private WeakReference<UIElement>? _pinned;
	private ulong _pinnedHandle;
	private ulong _pinnedAnchor;
	private string _pinnedWords = "";

	public TapOverlay(TapHandles handles, TapLog log)
	{
		_handles = handles;
		_log = log;
		_refresh = Refresh;
	}

	/// <summary>What the toolbar's buttons are called and do, in the order they are shown.</summary>
	public IEnumerable<(string Name, Action Action)> ToolbarActions() =>
	[
		("Select", () => Toggle(Pointing.Select)),
		("Rulers", () => Toggle(Pointing.Rulers)),
		("Deselect", () => Deselect()),
		("My XAML", () =>
		{
			_justMyMarkup = !_justMyMarkup;
			Chrome();
		}),
		("Changes", () => ShowChanges(!_changesShown)),
		("Ask", Ask),
	];

	// --- Installing and refreshing ---

	/// <summary>Put an overlay on every view, and keep refreshing until <see cref="Remove"/>.</summary>
	public void Install()
	{
		if (_installed)
			return;
		_installed = true;
		Cover();
		Diagnostics.Post(_refresh);
		_log.Write($"Overlay installed on {_views.Count} view(s).");
	}

	/// <summary>Take every overlay down, release the pointer, forget the pick and the marks, and stop refreshing.</summary>
	public void Remove()
	{
		_installed = false;
		SetPointing(Pointing.None);
		foreach (var view in _views.Values)
			Diagnostics.RemoveOverlay(view.Root);
		_views.Clear();
		_marked.Clear();
		ClearPick(null);
		_hover = null;
	}

	/// <summary>The views covered now, for the magnifier and the tests.</summary>
	public IReadOnlyCollection<TapOverlayView> Views => _views.Values;

	// Put an overlay on each view that has none, and drop the ones whose view is gone.
	private void Cover()
	{
		var live = new HashSet<UIView>();
		foreach (var host in Diagnostics.Hosts)
		{
			foreach (var view in host.Views)
			{
				live.Add(view);
				if (_views.ContainsKey(view))
					continue;
				var overlay = new TapOverlayView(view, this);
				Diagnostics.AddOverlay(view, overlay.Root);
				_views[view] = overlay;
			}
		}
		foreach (var gone in _views.Keys.Where(v => !live.Contains(v)).ToList())
		{
			Diagnostics.RemoveOverlay(_views[gone].Root);
			_views.Remove(gone);
		}
		Chrome();
	}

	private void Refresh()
	{
		if (!_installed)
			return;
		Cover();
		if (_pick is not null && (Picked is not { } picked || Diagnostics.Bounds(picked) is null))
			ClearPick(Removed);
		Redraw();
		Diagnostics.Post(_refresh);
	}

	// --- The pointer modes ---

	/// <summary>Arm picking, or rulers with <paramref name="rulers"/>; <paramref name="includeAll"/> picks elements that
	/// are not hit-testable too.</summary>
	public void Arm(bool rulers, bool includeAll)
	{
		_includeAll = includeAll;
		SetPointing(rulers ? Pointing.Rulers : Pointing.Select);
	}

	/// <summary>Leave whichever mode is on. The pick stays: clearing it is <see cref="Deselect"/>'s.</summary>
	public void GoIdle() => SetPointing(Pointing.None);

	/// <summary>The extent the capture layer was last arranged at, in DIPs, on the first view: zero while it has not been
	/// laid out, which a host reports as an armed mode nobody can point at.</summary>
	public (int Width, int Height) ArmedExtent()
	{
		var capture = _views.Values.FirstOrDefault(v => v.View.Interactive)?.Capture;
		return capture is null || capture.Visibility != Visibility.Visible
			? (0, 0)
			: ((int)capture.LayoutRect.Width, (int)capture.LayoutRect.Height);
	}

	/// <summary>Whether a pick prefers the app's own markup over a template's parts.</summary>
	public void SetJustMyMarkup(bool justMyMarkup)
	{
		_justMyMarkup = justMyMarkup;
		Chrome();
	}

	private void Toggle(Pointing mode) => SetPointing(_pointing == mode ? Pointing.None : mode);

	private void SetPointing(Pointing mode)
	{
		_pointing = mode;
		_hover = null;
		if (mode != Pointing.Rulers || _pickHandle != _pinnedAnchor)
			Unpin();
		var armed = mode != Pointing.None;
		foreach (var view in _views.Values)
			view.Capture.Visibility = armed ? Visibility.Visible : Visibility.Collapsed;

		// The host's pointer is held while a mode is armed, so the click that picks never reaches the game.
		foreach (var host in _held)
			Diagnostics.HoldPointer(host, false);
		_held.Clear();
		if (armed)
		{
			foreach (var host in Diagnostics.Hosts.Where(h => h.Views.Any(_views.ContainsKey)))
			{
				Diagnostics.HoldPointer(host, true);
				_held.Add(host);
			}
		}
		Redraw();
		Chrome();
	}

	/// <summary>A pointer move over the capture layer: what is under it, measured against the anchor in rulers mode.</summary>
	public void OnHover(TapOverlayView view, Vector2 point)
	{
		_hover = Beneath(view, point) is { } element ? new WeakReference<UIElement>(element) : null;
		Redraw();
	}

	/// <summary>
	/// A click on the capture layer. In picking it records the candidate stack under the pointer and takes the pick,
	/// then goes idle. In rulers with an anchor it pins the measurement to what was clicked, a second click on the pinned
	/// element anchors there instead, and a click on the anchor lets the pin go.
	/// </summary>
	public void OnPick(TapOverlayView view, Vector2 point)
	{
		var element = Beneath(view, point);
		if (_pointing == Pointing.Rulers && Picked is { } anchor)
		{
			if (element is null)
				return;
			if (ReferenceEquals(element, anchor))
			{
				Unpin();
				Redraw();
				return;
			}
			if (!(_pinned is not null && _pinned.TryGetTarget(out var pinned) && ReferenceEquals(pinned, element)))
			{
				Pin(element);
				Redraw();
				return;
			}
			Unpin();
		}

		if (element is not null)
		{
			var (rows, selected) = Candidates(element);
			TakePick(selected, rows);
		}
		if (_pointing == Pointing.Select)
			SetPointing(Pointing.None);
		else
			Redraw();
	}

	// --- The pick ---

	/// <summary>Pick the element <paramref name="handle"/> names, with no hit test: the element and its ancestors are its
	/// rows. False for a handle that names nothing on screen.</summary>
	public bool SelectByHandle(ulong handle)
	{
		if (_handles.Resolve(handle) is not { } element || Diagnostics.Bounds(element) is null)
			return false;
		var rows = new StringBuilder();
		var written = 0;
		for (var e = element; e is not null && written < MaxCandidates; e = Diagnostics.ParentOf(e), written++)
			rows.Append(Candidate(e)).Append('\n');
		TakePick(element, rows.ToString());
		return true;
	}

	/// <summary>Clear the pick; false when there was none.</summary>
	public bool Deselect()
	{
		var had = _pick is not null;
		ClearPick(null);
		return had;
	}

	/// <summary>The reply to <c>selection</c>: the header row, then the candidates, topmost first.</summary>
	public string Selection() =>
		TapRows.SelectionHeader(ModeName, _justMyMarkup, _gone, _changesShown) + "\n" + _rows;

	private string ModeName => _pointing switch
	{
		Pointing.Select => "select",
		Pointing.Rulers => "rulers",
		_ => "idle",
	};

	private UIElement? Picked => _pick is not null && _pick.TryGetTarget(out var element) ? element : null;

	private void TakePick(UIElement element, string rows)
	{
		_pick = new WeakReference<UIElement>(element);
		_pickHandle = _handles.HandleOf(element);
		_rows = rows;
		_gone = "";
		if (_pinnedAnchor != _pickHandle)
			Unpin();
		Redraw();
		Chrome();
	}

	private void ClearPick(string? gone)
	{
		var had = _pick is not null;
		_pick = null;
		_pickHandle = 0;
		_rows = "";
		Unpin();
		if (gone is not null && had)
			_gone = gone;
		Redraw();
		Chrome();
	}

	// The element under a point in a view, through the overlay, or null.
	private UIElement? Beneath(TapOverlayView view, Vector2 point) => Diagnostics.HitTest(view.View, point, _includeAll);

	// The candidate stack under a click, topmost first: what was hit, then each element it sits in. The pick is the
	// topmost, or with just-my-markup on the topmost the app's own markup declared, where there is one.
	private (string Rows, UIElement Selected) Candidates(UIElement hit)
	{
		var rows = new StringBuilder();
		UIElement? app = null;
		var written = 0;
		for (var e = hit; e is not null && written < MaxCandidates; e = Diagnostics.ParentOf(e), written++)
		{
			rows.Append(Candidate(e)).Append('\n');
			if (app is null && Diagnostics.SourceOf(e) is { InTemplate: false })
				app = e;
		}
		return (rows.ToString(), _justMyMarkup && app is not null ? app : hit);
	}

	private string Candidate(UIElement element) =>
		TapRows.Candidate(_handles.HandleOf(element), TapTree.TypeName(element.GetType()), TapTree.NameOf(element),
			element.GetType().Assembly == typeof(UIElement).Assembly);

	/// <summary>An element as a caption names it: its short type, and its name where it has one.</summary>
	public static string Describe(UIElement element) =>
		TapTree.NameOf(element) is { Length: > 0 } name ? $"{element.GetType().Name} · {name}" : element.GetType().Name;

	// --- Rulers ---

	private void Pin(UIElement element)
	{
		if (Picked is not { } anchor || Diagnostics.Bounds(anchor) is not { } from || Diagnostics.Bounds(element) is not { } to)
			return;
		_pinned = new WeakReference<UIElement>(element);
		_pinnedHandle = _handles.HandleOf(element);
		_pinnedAnchor = _pickHandle;
		_pinnedWords = TapMeasure.Words(Describe(anchor), Describe(element),
			TapMeasure.Measure(from.X, from.Right, to.X, to.Right), TapMeasure.Measure(from.Y, from.Bottom, to.Y, to.Bottom));
		Chrome();
	}

	private void Unpin()
	{
		_pinned = null;
		_pinnedHandle = 0;
		_pinnedAnchor = 0;
		_pinnedWords = "";
	}

	// --- Change marks ---

	/// <summary>Take what an apply changed. A change to an element already marked moves its tag to the newest change and
	/// starts its time again; the oldest element is let go when more are marked than the overlay draws.</summary>
	public void Record(IReadOnlyList<TapChange> changes)
	{
		foreach (var change in changes)
		{
			if (!_marked.TryGetValue(change.Handle, out var marked))
				_marked[change.Handle] = marked = new Marked();
			marked.Since = DateTime.UtcNow;
			marked.Changes.Add(change);
			if (marked.Changes.Count > MaxChangesPerElement)
				marked.Changes.RemoveAt(0);
		}
		while (_marked.Count > MaxMarkedElements)
			_marked.Remove(_marked.MinBy(m => m.Value.Since).Key);
		Redraw();
	}

	/// <summary>Whether the change marks are drawn.</summary>
	public bool ChangesShown => _changesShown;

	/// <summary>Switch the change marks. On draws every change this session made, each for another ten minutes.</summary>
	public void ShowChanges(bool shown)
	{
		if (shown && !_changesShown)
		{
			foreach (var marked in _marked.Values)
				marked.Since = DateTime.UtcNow;
		}
		_changesShown = shown;
		Redraw();
		Chrome();
	}

	// --- Ask ---

	/// <summary>What Ask would send now: a pinned measurement first, then the lens's colour, then the pick; null with
	/// nothing to send.</summary>
	public (string Kind, string Readable, ulong Element, ulong Other, string Colour)? AskAbout()
	{
		if (_pointing == Pointing.Rulers && _pinnedWords.Length > 0)
			return ("measurement", _pinnedWords, _pickHandle, _pinnedHandle, "");
		if (Picked is { } picked)
			return ("element", $"the element {Describe(picked)} picked in the game", _pickHandle, 0, "");
		return null;
	}

	private void Ask()
	{
		if (AskAbout() is not { } subject)
			return;
		TapAsk.Raise(subject.Kind, subject.Readable, subject.Element, subject.Other, subject.Colour);
		_log.Write($"Asked about {subject.Readable}.");
		Chrome();
	}

	// --- Drawing ---

	/// <summary>Paint the toolbar: the mode buttons lit for the mode that is on, the toggles for their state, and Ask lit
	/// while a press waits for the host.</summary>
	public void Chrome()
	{
		var canAsk = AskAbout() is not null;
		foreach (var view in _views.Values)
		{
			view.Paint("Select", _pointing == Pointing.Select);
			view.Paint("Rulers", _pointing == Pointing.Rulers);
			view.Paint("Deselect", false, _pick is not null);
			view.Paint("My XAML", _justMyMarkup);
			view.Paint("Changes", _changesShown);
			view.Paint("Ask", TapAsk.Waiting, canAsk);
		}
	}

	// Place every mark from where its element is now.
	private void Redraw()
	{
		var hover = _hover is not null && _hover.TryGetTarget(out var hovered) ? hovered : null;
		var picked = Picked;
		var pinned = _pinned is not null && _pinned.TryGetTarget(out var pin) ? pin : null;
		var measuredTo = _pointing == Pointing.Rulers ? pinned ?? hover : null;
		var rulers = Rulers(picked, measuredTo);

		foreach (var view in _views.Values)
		{
			var hoverRect = hover is not null && _pointing != Pointing.None ? BoundsIn(view, hover) : null;
			view.Place(view.HoverBox, hoverRect);
			view.Label(view.HoverBadge, hoverRect, hover is null ? "" : Describe(hover));

			var pickRect = picked is not null ? BoundsIn(view, picked) : null;
			view.Place(view.PickBox, pickRect);
			view.Label(view.PickBadge, pickRect, picked is null ? "" : Describe(picked));

			view.RulersText.Visibility = _pointing == Pointing.Rulers ? Visibility.Visible : Visibility.Collapsed;
			view.RulersText.Text = rulers;
			DrawLeaders(view, pickRect, measuredTo is not null && !ReferenceEquals(measuredTo, picked) ? BoundsIn(view, measuredTo) : null);

			DrawChanges(view);
		}
	}

	// The rulers row: the anchor's size, margin and padding, and the pinned measurement.
	private string Rulers(UIElement? anchor, UIElement? to)
	{
		if (_pointing != Pointing.Rulers)
			return "";
		if (anchor is null || Diagnostics.Bounds(anchor) is not { } rect)
			return "click an element to anchor";
		var text = $"{TapMeasure.Number(rect.Width)}×{TapMeasure.Number(rect.Height)}";
		if (anchor is FrameworkElement framework)
			text += "  Margin " + Edges(framework.Margin);
		text += Thickness(anchor, "Padding") is { } padding ? "  Padding " + Edges(padding) : "  Padding none";
		if (_pinnedWords.Length > 0 && to is not null && Diagnostics.Bounds(to) is { } other)
		{
			text += $"  Pinned to {Describe(to)}: ↔ {TapMeasure.Numbers(TapMeasure.Measure(rect.X, rect.Right, other.X, other.Right))}"
				+ $"  ↕ {TapMeasure.Numbers(TapMeasure.Measure(rect.Y, rect.Bottom, other.Y, other.Bottom))}";
		}
		return text;
	}

	// A dimension line per axis where the two miss each other, two where they overlap, each with its number.
	private static void DrawLeaders(TapOverlayView view, Rect? anchor, Rect? other)
	{
		var used = 0;
		if (anchor is { } a && other is { } o)
		{
			var horizontal = TapMeasure.Measure(a.X, a.Right, o.X, o.Right);
			var vertical = TapMeasure.Measure(a.Y, a.Bottom, o.Y, o.Bottom);
			var alongY = TapMeasure.Cross(a.Y, a.Bottom, o.Y, o.Bottom);
			var alongX = TapMeasure.Cross(a.X, a.Right, o.X, o.Right);

			if (horizontal.Separated)
			{
				var from = horizontal.Before ? o.Right : a.Right;
				Leader(view, used++, from, alongY, horizontal.Gap, horizontal: true);
			}
			else
			{
				Leader(view, used++, MathF.Min(a.X, o.X), alongY, MathF.Abs(horizontal.Low), horizontal: true);
				Leader(view, used++, MathF.Min(a.Right, o.Right), alongY, MathF.Abs(horizontal.High), horizontal: true);
			}

			if (vertical.Separated)
			{
				var from = vertical.Before ? o.Bottom : a.Bottom;
				Leader(view, used++, alongX, from, vertical.Gap, horizontal: false);
			}
			else
			{
				Leader(view, used++, alongX, MathF.Min(a.Y, o.Y), MathF.Abs(vertical.Low), horizontal: false);
				Leader(view, used++, alongX, MathF.Min(a.Bottom, o.Bottom), MathF.Abs(vertical.High), horizontal: false);
			}
		}

		view.Place(view.AnchorBox, used > 0 ? anchor : null);
		for (var i = used; i < view.Leaders.Length; i++)
		{
			view.Leaders[i].Visibility = Visibility.Collapsed;
			view.LeaderLabels[i].Visibility = Visibility.Collapsed;
		}
	}

	private static void Leader(TapOverlayView view, int index, float x, float y, float length, bool horizontal)
	{
		var line = horizontal ? new Rect(x, y, length, 1f) : new Rect(x, y, 1f, length);
		view.Place(view.Leaders[index], line);
		view.LabelAt(view.LeaderLabels[index], horizontal ? x + length / 2f : x + 3f, horizontal ? y + 3f : y + length / 2f,
			TapMeasure.Number(length));
	}

	private void DrawChanges(TapOverlayView view)
	{
		var now = DateTime.UtcNow;
		var drawn = new List<(Rect Box, string Tag)>();
		if (_changesShown)
		{
			foreach (var (handle, marked) in _marked)
			{
				if (now - marked.Since > MarkLifetime || _handles.Resolve(handle) is not { } element || BoundsIn(view, element) is not { } rect)
					continue;
				var newest = marked.Changes[^1];
				var tag = $"{newest.Property}: {Cut(newest.Old)} → {Cut(newest.New)} ({Ago(now - newest.At)})";
				if (marked.Changes.Count > 1)
					tag += $" +{marked.Changes.Count - 1} more";
				drawn.Add((rect, tag));
			}
		}

		var marks = view.ChangeMarks(drawn.Count);
		for (var i = 0; i < drawn.Count; i++)
		{
			view.Place(marks[i].Box, drawn[i].Box);
			view.LabelAt(marks[i].Tag, drawn[i].Box.X, drawn[i].Box.Bottom + 2f, drawn[i].Tag);
		}
	}

	// An element's bounds where it is in this view, or null where it is in another or in none.
	private Rect? BoundsIn(TapOverlayView view, UIElement element)
	{
		var top = element;
		while (Diagnostics.ParentOf(top) is { } parent)
			top = parent;
		return Diagnostics.ChildrenOf(view.View).Contains(top) ? Diagnostics.Bounds(element) : null;
	}

	private static Thickness? Thickness(UIElement element, string name) =>
		element.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(element) is Thickness thickness ? thickness : null;

	private static string Edges(Thickness t) =>
		$"{TapMeasure.Number(t.Left)},{TapMeasure.Number(t.Top)},{TapMeasure.Number(t.Right)},{TapMeasure.Number(t.Bottom)}";

	private static string Cut(string value) => value.Length <= 24 ? (value.Length == 0 ? "(cleared)" : value) : value[..23] + "…";

	private static string Ago(TimeSpan age) => age.TotalMinutes >= 1 ? $"{(int)age.TotalMinutes}m ago" : $"{(int)age.TotalSeconds}s ago";

	// One element's marked changes, and when its ten minutes started.
	private sealed class Marked
	{
		public DateTime Since;
		public List<TapChange> Changes { get; } = [];
	}
}
