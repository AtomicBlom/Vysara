using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using RoseMcp.Tap.Protocol;

using SUIsei.Controls;

namespace SUIsei.RoseTap;

/// <summary>
/// What a person sees of the tap in their game: a toolbar on each view, picking, the hover and pick outlines, rulers,
/// the marks on what live edits changed, the Ask button, and the magnifier (<see cref="TapZoom"/>). The same modes and
/// replies as the XAML taps' toolbar, built from SUIsei's own elements as one diagnostics overlay per view.
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
	// What the last tick drew from, so a tick that changes nothing reads no host list and builds no text. The views are
	// covered again when SUIsei's views version moves or a covered host is collected. Captions are kept per element. The
	// rulers row is rebuilt when the stamp moves, which every change to the mode, the pick, the pin or the hover does, or
	// when a measured element's bounds move.
	private int _coveredVersion = -1;
	private readonly HashSet<UIView> _liveViews = [];
	private readonly List<UIView> _goneViews = [];
	private readonly List<(Rect Box, string Tag)> _drawnChanges = [];
	private readonly ConditionalWeakTable<UIElement, string> _captions = new();
	private int _stamp;
	private (int Stamp, Rect? Anchor, Rect? To) _rulersKey = (-1, null, null);
	private string _rulersText = "";

	public TapOverlay(TapHandles handles, TapLog log)
	{
		_handles = handles;
		_log = log;
		_refresh = Refresh;
	}

	/// <summary>The magnifier, whose lens Ask can send a colour from.</summary>
	public TapZoom Zoom { get; } = new();

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
		("Zoom", () => Zoom.Toggle(this)),
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
		Zoom.Off(this);
		SetPointing(Pointing.None);
		foreach (var view in _views.Values)
			Diagnostics.RemoveOverlay(view.Root);
		_views.Clear();
		_coveredVersion = -1;
		_marked.Clear();
		ClearPick(null);
		_hover = null;
	}

	/// <summary>The views covered now, for the magnifier and the tests. The dictionary's own collection, so walking it each
	/// tick allocates nothing.</summary>
	public Dictionary<UIView, TapOverlayView>.ValueCollection Views => _views.Values;

	// Put an overlay on each view that has none, and drop the ones whose view is gone. The hosts are read only when the
	// views have changed or a covered host has been collected, so a tick that changes neither allocates nothing.
	private void Cover()
	{
		var version = Diagnostics.ViewsVersion;
		if (version == _coveredVersion && !AnyHostCollected())
			return;
		_coveredVersion = version;

		foreach (var host in Diagnostics.Hosts)
		{
			foreach (var view in host.Views)
			{
				_liveViews.Add(view);
				if (_views.ContainsKey(view))
					continue;
				var overlay = new TapOverlayView(host, view, this);
				Diagnostics.AddOverlay(view, overlay.Root);
				_views[view] = overlay;
			}
		}
		foreach (var (view, overlay) in _views)
		{
			if (!_liveViews.Contains(view) || overlay.Host is null)
				_goneViews.Add(view);
		}
		foreach (var view in _goneViews)
		{
			Diagnostics.RemoveOverlay(_views[view].Root);
			_views.Remove(view);
		}
		_liveViews.Clear();
		_goneViews.Clear();
		Chrome();
	}

	private bool AnyHostCollected()
	{
		foreach (var view in _views.Values)
		{
			if (view.Host is null)
				return true;
		}
		return false;
	}

	private void Refresh()
	{
		if (!_installed)
			return;
		Cover();
		if (_pick is not null && (Picked is not { } picked || Diagnostics.Bounds(picked) is null))
			ClearPick(Removed);
		Redraw();
		Zoom.Refresh(this);
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
		_stamp++;
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
		var element = Beneath(view, point);
		var hovered = _hover is not null && _hover.TryGetTarget(out var current) ? current : null;
		if (!ReferenceEquals(element, hovered))
		{
			_hover = element is not null ? new WeakReference<UIElement>(element) : null;
			_stamp++;
		}
		Zoom.PointerAt(view, point);
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
		_stamp++;

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
		_stamp++;

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
		_stamp++;

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
		_stamp++;

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
			marked.Tag = null;
			marked.Changes.Add(change);
			if (marked.Changes.Count > MaxChangesPerElement)
				marked.Changes.RemoveAt(0);
		}
		while (_marked.Count > MaxMarkedElements)
			_marked.Remove(_marked.MinBy(m => m.Value.Since).Key);

		// An edit can rename an element or change the margin and padding the rulers row reads.
		_captions.Clear();
		_stamp++;
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
		if (Zoom.Colour is { } colour)
			return ("colour", $"the colour {colour.Hex} under the pixel lens, at {colour.X},{colour.Y} in the game's surface", 0, 0, colour.Hex);
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
			view.Paint("Zoom", Zoom.On);
		}
	}

	// Place every mark from where its element is now. Text is built only where what it says has changed, so a tick on
	// which nothing moved allocates nothing.
	private void Redraw()
	{
		var hover = _hover is not null && _hover.TryGetTarget(out var hovered) ? hovered : null;
		var picked = Picked;
		var pinned = _pinned is not null && _pinned.TryGetTarget(out var pin) ? pin : null;
		var measuredTo = _pointing == Pointing.Rulers ? pinned ?? hover : null;

		var rulersKey = (_stamp, picked is null ? null : Diagnostics.Bounds(picked), measuredTo is null ? null : Diagnostics.Bounds(measuredTo));
		if (rulersKey != _rulersKey)
		{
			_rulersKey = rulersKey;
			_rulersText = Rulers(picked, measuredTo);
		}
		TagChanges();

		foreach (var view in _views.Values)
		{
			var hoverRect = hover is not null && _pointing != Pointing.None ? BoundsIn(view, hover) : null;
			view.Place(view.HoverBox, hoverRect);
			view.Label(view.HoverBadge, hoverRect, hover is null ? "" : Caption(hover));

			var pickRect = picked is not null ? BoundsIn(view, picked) : null;
			view.Place(view.PickBox, pickRect);
			view.Label(view.PickBadge, pickRect, picked is null ? "" : Caption(picked));

			view.RulersText.Visibility = _pointing == Pointing.Rulers ? Visibility.Visible : Visibility.Collapsed;
			view.RulersText.Text = _rulersText;
			DrawLeaders(view, pickRect, measuredTo is not null && !ReferenceEquals(measuredTo, picked) ? BoundsIn(view, measuredTo) : null);

			DrawChanges(view);
		}
	}

	// An element's caption, built once and kept until an edit could have renamed it.
	private string Caption(UIElement element)
	{
		if (!_captions.TryGetValue(element, out var caption))
		{
			caption = Describe(element);
			_captions.AddOrUpdate(element, caption);
		}
		return caption;
	}

	// Each marked element's tag, rebuilt where it has none or the age it shows has moved on; a mark whose ten minutes are
	// up has none, and is not drawn.
	private void TagChanges()
	{
		if (!_changesShown)
			return;
		var now = DateTime.UtcNow;
		foreach (var marked in _marked.Values)
		{
			if (now - marked.Since > MarkLifetime)
			{
				marked.Tag = null;
				continue;
			}
			var newest = marked.Changes[^1];
			var age = now - newest.At;
			var shown = age.TotalMinutes >= 1 ? -(int)age.TotalMinutes : (int)age.TotalSeconds;
			if (marked.Tag is not null && marked.TagAge == shown)
				continue;
			var tag = $"{newest.Property}: {Cut(newest.Old)} → {Cut(newest.New)} ({Ago(age)})";
			if (marked.Changes.Count > 1)
				tag += $" +{marked.Changes.Count - 1} more";
			marked.Tag = tag;
			marked.TagAge = shown;
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
		var label = view.LeaderLabels[index];
		if (view.LeaderLengths[index] != length)
		{
			view.LeaderLengths[index] = length;
			((TextBlock)label.Child!).Text = TapMeasure.Number(length);
		}
		view.LabelAt(label, horizontal ? x + length / 2f : x + 3f, horizontal ? y + 3f : y + length / 2f, ((TextBlock)label.Child!).Text);
	}

	private void DrawChanges(TapOverlayView view)
	{
		_drawnChanges.Clear();
		if (_changesShown)
		{
			foreach (var (handle, marked) in _marked)
			{
				if (marked.Tag is not { } tag || _handles.Resolve(handle) is not { } element || BoundsIn(view, element) is not { } rect)
					continue;
				_drawnChanges.Add((rect, tag));
			}
		}

		var marks = view.ChangeMarks(_drawnChanges.Count);
		for (var i = 0; i < _drawnChanges.Count; i++)
		{
			view.Place(marks[i].Box, _drawnChanges[i].Box);
			view.LabelAt(marks[i].Tag, _drawnChanges[i].Box.X, _drawnChanges[i].Box.Bottom + 2f, _drawnChanges[i].Tag);
		}
	}

	// An element's bounds where it is in this view, or null where it is in another or in none.
	private static Rect? BoundsIn(TapOverlayView view, UIElement element) =>
		ReferenceEquals(Diagnostics.ViewOf(element), view.View) ? Diagnostics.Bounds(element) : null;

	private static Thickness? Thickness(UIElement element, string name) =>
		element.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(element) is Thickness thickness ? thickness : null;

	private static string Edges(Thickness t) =>
		$"{TapMeasure.Number(t.Left)},{TapMeasure.Number(t.Top)},{TapMeasure.Number(t.Right)},{TapMeasure.Number(t.Bottom)}";

	private static string Cut(string value) => value.Length <= 24 ? (value.Length == 0 ? "(cleared)" : value) : value[..23] + "…";

	private static string Ago(TimeSpan age) => age.TotalMinutes >= 1 ? $"{(int)age.TotalMinutes}m ago" : $"{(int)age.TotalSeconds}s ago";

	// One element's marked changes, and when its ten minutes started. Its tag is kept with the age it shows, so the text is
	// rebuilt when that age moves on, at most once a second, rather than every tick; null where it needs building.
	private sealed class Marked
	{
		public DateTime Since;
		public string? Tag;
		public int TagAge;
		public List<TapChange> Changes { get; } = [];
	}
}
