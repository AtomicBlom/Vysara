# XAML Hot Reload

SUIsei views can be edited live: change a `.xaml` while the app runs and the affected view rebuilds in place,
no restart. It rides .NET's metadata-update (Hot Reload) mechanism — the same one that applies `.cs` edits — so
there is no XAML-specific tooling to install.

## How it works

1. The build registers each `.xaml` as an `AdditionalFiles` input to the `SUIsei.SourceGen` incremental
   generator, and (via `build/SUIsei.targets`) as a `Watch` item so `dotnet watch` re-runs the generator on edit.
2. Editing a `.xaml` re-runs the generator, producing a new `InitializeComponent()` body that ships in the
   metadata delta.
3. Each generated view registers itself with the `HotReload` registry (a no-capture, weak reference) in
   `InitializeComponent()`, and each `UIHost` registers itself on construction.
4. When the delta is applied, the `[MetadataUpdateHandler]` records the changed types. The **next**
   `UIHost.Tick` drains them on the UI thread and calls the generated `RebuildComponent()` of each view of those
   types in a live host's tree, outer views first. It detaches the old `{x:Bind}` subscriptions, rebuilds a fresh
   tree + bindings, puts a new root where the old one was (`HotReload.ReplaceRoot`), and calls the code-behind's
   `OnComponentRebuilt()`.
5. The replaced tree leaves the host, so its elements stop listening to the brushes and transforms they showed: a
   brush that outlives the rebuild (a static, or one in a dictionary the rebuild keeps) holds none of the old tree.

The whole mechanism is gated on `HotReload.Enabled`, which defaults to `MetadataUpdater.IsSupported`: it is
`false` under NativeAOT / an optimized publish, so the registry, the handler, and the per-view `Track` call are
all inert and trimmable in a shipped game.

## Running

    dotnet watch run --project Loom.Sandbox

Edit a `.xaml` and save; the view refreshes on the next frame.

## What reloads, and what needs a restart

`MetadataUpdater` applies *method-body* changes cleanly; some shape changes are "rude edits" the runtime cannot
apply live and that force a restart.

**Reloads live:**

- Changing literal/enum property values (`Text`, `FontSize`, `Background`, alignment, margins…).
- Adding, removing, reordering, or re-nesting elements that have **no** `x:Name`.
- Adding/removing/retargeting `{x:Bind}` and `{StaticResource}` on existing named elements.
- Editing styles, templates, storyboards, and visual states.

A `DataTemplate` whose `OneWay` or `TwoWay` bindings change keeps reloading: its bindings helper is a class nested in the
view's, which a new binding gives a field, and its elements are handed to the helper by an object initializer, never its
constructor, so no signature changes. Rows already realized keep the template they were built from until their items
control realizes them again (the outer view's rebuild does).

**Needs a restart (rude edit):**

- Adding or renaming an `x:Name` (adds/renames a generated field).
- Wiring a new event handler attribute (adds a generated subscription) or changing a handler signature.
- Changing the root `x:Class` or a template's `x:DataType`.

## State set in code: `OnComponentRebuilt`

A rebuild makes every element of the view again, so whatever the code-behind set on its `x:Name`'d elements in code
is gone: points set with `PointCollection.SetAll`, `Canvas.Left`/`Top`, which visual state a group is in, children
built in code inside a named container. The generated partial declares

```csharp
partial void OnComponentRebuilt();
```

and `RebuildComponent()` calls it last, once the rebuilt tree, its bindings and (for a composition view) its new
`Root` are in place. Implement it to re-apply that state; left unimplemented, it compiles to nothing. It runs on
every rebuild, never on the first `InitializeComponent()`. The pattern is to keep what the code-behind shows in
fields, apply it from one method, and call that method both from the code that changes it and from the hook:

```csharp
partial class StatsPanel
{
	private Point[] _trace = [];
	private bool _expanded;

	public void Show(Point[] trace, bool expanded)
	{
		(_trace, _expanded) = (trace, expanded);
		Apply(useTransitions: true);
	}

	// The rebuild made new elements: put back what code set on them.
	partial void OnComponentRebuilt() => Apply(useTransitions: false);

	private void Apply(bool useTransitions)
	{
		Trace.Points.SetAll(_trace);
		Canvas.SetLeft(Marker, _trace.Length > 0 ? _trace[^1].X : 0f);
		VisualStateManager.GoToState(Card, _expanded ? "Expanded" : "Collapsed", useTransitions);
	}
}
```

A view nested in this one's XAML is made again too, so re-apply what the code-behind gave it as well; its own
constructor has already run its `InitializeComponent()`. `RebuildComponent()` called directly, without hot reload,
does the same: the new root takes the old one's place and the hook runs.

## Scope

Hot reload rebuilds a view wherever it sits in a live `UIHost`'s tree: hosted as a `UIRoot.Content`, nested in
another view's XAML (`<local:StatsPanel x:Name="Stats" />`) or put in a container by code, at any depth, and in an
open popup. A view in no live host's tree (constructed but never hosted, taken out of its parent, or under a `UIRoot`
no `UIHost` owns) is skipped: there is nothing on screen to refresh. Whether a view is live is decided as its turn
comes, not up front, so when an outer view and a view nested in it both change, the outer one rebuilds first and the
inner instance it replaced is skipped; the new inner instance was built from the new XAML already.

How a view rebuilds depends on its root:

- **An inheritance view** (an unsealed root such as `ContentControl`, which the view derives from) rebuilds on the
  same object, so its parent needs nothing and it keeps its place.
- **A composition view** (a sealed root such as `StackPanel`, `Grid`, `Canvas` or `Border`, which the view holds as
  `Root`) gets a new `Root`, which takes the old one's place in its parent: at the same index in a `Panel`'s
  `Children`, as a `Border`'s `Child`, as a `ScrollViewer`'s or a `ContentControl`'s `Content` (a templated
  control's too, whose `ContentPresenter` shows it), as a `ContentPresenter`'s `Content`, or as a `UIRoot`'s
  `Content`. A composition view whose root sits anywhere else is **skipped**, left as it was:
  - a `Popup`'s `Child`: an open popup's child is an overlay of the root, with no link back to its popup;
  - an item of an `ItemsControl` or a `ListView`: its `ItemsSource` would only put the old root back;
  - any other parent.

Attached properties the parent's code set on the old root (`Grid.SetRow`, `Canvas.SetLeft`) are not carried to the
new one. Set them on the root in the view's own XAML, or have the view re-apply them in `OnComponentRebuilt`.

A composition view can only be put in its place by code: its class is not a `UIElement`, so XAML can't nest it. To
nest a view in XAML, give it an unsealed root (a `ContentControl`) and name that base in the code-behind too
(`partial class StatsPanel : ContentControl`). The generator reading the outer view's XAML sees only the hand-written
half of the inner view, so without the base an attribute on `<local:StatsPanel>` (a `Margin`, a `Grid.Row`) is
SUISEI004.

Transient per-view UI state (a pressed button, an in-flight animation) is reset by a rebuild — expected for a
dev-loop refresh.
