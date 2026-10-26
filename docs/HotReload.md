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
   `UIHost.Tick` drains them on the UI thread, calls the generated `RebuildComponent()` (which detaches the old
   `{x:Bind}` subscriptions, then rebuilds a fresh tree + bindings), and re-hosts the new root.
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

**Needs a restart (rude edit):**

- Adding or renaming an `x:Name` (adds/renames a generated field).
- Wiring a new event handler attribute (adds a generated subscription) or changing a handler signature.
- Changing the root `x:Class` or a template's `x:DataType`.

## Scope

Hot reload refreshes a view that is hosted as a `UIRoot.Content` (the standard `uiRoot.Content = view.Root`
pattern). A view that is constructed but not hosted in a live `UIHost` is left untouched until it is hosted.
Transient per-view UI state (a pressed button, an in-flight animation) is reset by a rebuild — expected for a
dev-loop refresh.
