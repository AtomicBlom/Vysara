# SUIsei — Design Bible & Roadmap

> **What this is.** The design bible for SUIsei: the locked decisions, the architecture, the public contracts an integrating engine depends on, the load-bearing pillars, and the work that remains. [`README.md`](README.md) is the front page (features + usage); this is the *why* and the *what's next*.
>
> **What SUIsei is.** A standalone, **GPU-free, NativeAOT-clean**, retained-mode, WinUI-3-inspired XAML UI runtime for .NET 10, plus a Roslyn XAML source generator. It knows nothing about any render graph: it produces a neutral draw list (or exposes its composition tree directly) and a host renders it. In the Loom engine it is consumed by the thin `Loom.UI.SUIsei` glue through Loom's neutral `IRenderContext.DrawList` verb — the same path Dear ImGui uses — so no UI type ever enters the engine's describe/execute layers.

---

## 1. Locked decisions

| Axis | Decision |
|---|---|
| Dialect | **WinUI 3** (`Microsoft.UI.Xaml`) syntax — a curated, published *subset* (§4). |
| Binding | **`{x:Bind}` only**, compiled. No reflection `{Binding}`. |
| Runtime | **NativeAOT-clean** end to end — no reflection in the property system, binding, layout, or hot paths. |
| Precompiler | **One Roslyn incremental source generator** consuming `.xaml` as `AdditionalFiles`; emits tree construction + typed `x:Bind`/event/storyboard/visual-state glue. No MSBuild task. |
| Text | **MSDF/MTSDF prebaked glyph atlases**; runtime shaping behind a swappable `ITextShaper`. |
| Composition | **Retained `Visual` tree** behind the element tree, rendered by a pluggable **`IUICompositor`** — the engine-portability seam. SUIsei's default `DrawListCompositor` flattens to neutral draw data. |
| Surfaces | Screen-space overlay **and** in-world (the in-world quad math lives in the *host glue*, keeping SUIsei purely 2D/neutral). |
| Colour / HDR | Authoring uses a float `HdrColor` (`0..1` = SDR/paper-white, `>1` = above paper-white, opt-in); composited as premultiplied **RGBA16F**, pinned to a configurable paper-white reference. |
| Hosting | `UIHost` is **per-window, not a singleton**; holds no engine handle. |
| Testability | **Headless-testable by construction** — every logic subsystem is unit-tested on CPU with no GPU/window. |

---

## 2. Architecture

**Two retained trees and a pluggable compositor.** The UI is a retained **element tree** (dependency properties / layout / controls) that drives a separate retained **composition `Visual` tree** (transform / clip / opacity / brush / content), which a pluggable **`IUICompositor`** turns into rendering. This is WinUI's element-tree-vs-composition-tree split, made into the portability seam:

- **The default `DrawListCompositor`** walks the visual tree, resolves transform/clip/opacity into a pre-transformed vertex + command stream, and **recomposites only dirty subtrees**. It produces a neutral `DrawListData`.
- **A foreign engine** implements its own `IUICompositor`, consumes the `Visual` tree directly, and maps it onto its own scene — taking the whole element/layout/input/animation half and substituting only the compositor. The `Visual` tree is the neutral contract one level above the draw list.

### The neutral draw-list contract (load-bearing)
SUIsei carries its own draw types in a `SUIsei.Drawing` namespace: `OverlayVertex` (position, uv, premultiplied HDR colour, custom params) and `DrawCommand` (clip rect, texture index, index range, a `uint` tag). **These two layouts are byte-identical to the host's structurally-equivalent types**, so the host glue zero-copy-casts (`MemoryMarshal.Cast`) rather than translating. `DrawListData` (the vertex/index/command spans + texture slot table + surface size + HDR flag + revision + texture revision + damage rect) is **SUIsei-private** — the painter unpacks its fields. **The frozen contract is the `OverlayVertex` + `DrawCommand` memory layout**; keep them in lockstep with the host's neutral types (no analyzer guards this today — see §7). Indices are 16-bit and relative to each command's `VertexOffset`, which the host passes as the base vertex: a surface past 65,535 vertices is split into index windows, so `VertexOffset` is not always 0 (a layer marker's `VertexOffset` is instead the absolute index of its tint vertex). The compositor double-buffers each surface's streams and hands out `DrawListData` as views over them, so a recomposite allocates nothing: a list stays readable through its surface's next recomposite and its streams throw once the one after that reuses its buffers. A host reads the current list each frame rather than keeping one.

**Texture slots.** A command's `TextureIndex` indexes its own list's slot table, `DrawListData.TextureSlots`, not any global id.
- **What a slot holds:** a `TextureSource`, a neutral description the host resolves: RGBA8 texels, a size, and a `Version` that moves when the texels change.
- **Slot 0** is always `TextureSource.White`, the white texel untextured commands sample.
- **The other slots** hold every source the list's glyph and image commands sample, each once, numbered densely in the order the walk first uses them. Glyph atlas pages are ordinary sources there, so a host always binds the atlas the glyphs were laid out against.
- **Every `TextureIndex` is in range:** each one the compositor emits is below its list's `TextureSlots.Count`. A host treats one outside it as a broken list and fails loud rather than sampling something else. Loom's painter refuses it, naming the command and surface; the CPU rasterizer skips and counts it.
- **`TextureRevision`** moves when the table names different sources or slots, or a source in it changes its `Version`. It also moves on a clean frame that re-submits the same list. A geometry-only recomposite leaves it alone, so a host rebinds and re-uploads only when it moves.
- **Cost:** slots are assigned with one reference-keyed lookup per primitive into retained storage, so a steady frame allocates nothing.

### Dependency floor + satellite projects
The **core `SUIsei` runtime depends on `StbImageSharp` alone** (to decode baked atlas PNGs) — nothing else, no GPU, no engine. Heavier capabilities are **separate satellite projects** precisely to keep the core dependency-light and NAOT-clean:

| Project | Role | Wired into the host build? |
|---|---|---|
| `SUIsei` | core runtime (DP model, controls, layout, composition, routed events, focus, hit-test, animation, `FontAtlas`, `ITextShaper` seam, default `SimpleTextShaper`) | yes |
| `SUIsei.SourceGen` | Roslyn `IIncrementalGenerator` (`netstandard2.0`), ships as an analyzer | yes |
| `SUIsei.Tests` | headless unit + source-gen snapshot tests (TUnit + Shouldly), no GPU | yes |
| `SUIsei.Rasterizer` | a CPU software rasterizer — the byte-identical oracle for compositor/group-opacity tests | test-only |
| `SUIsei.HarfBuzz` | HarfBuzzSharp text shaper (an `ITextShaper`); native assets currently Win32-only | satellite |
| `SUIsei.Text` | a **native managed MTSDF atlas generator** + OpenType reader (glyf + CFF) + atlas baker | not yet wired |
| `SUIsei.Text.Tool` | build-time bake tool driving `SUIsei.Text` | not yet wired |
| `SUIsei.Gallery` | demo application | standalone |

### Text: atlas generation and shaping
Text is **glyph-id-keyed MTSDF**, generated natively and shaped behind a swappable seam — both halves chosen to stay cross-platform, dependency-light, and NativeAOT-clean.

- **Glyph-id atlas + two independent seams.** `FontAtlas` keys glyphs by glyph id (keeping a codepoint→gid `cmap` for the no-shaping fast path and caret math), because ligatures and contextual forms are glyph ids with no codepoint. An `ITextShaper` turns a text run into positioned glyph ids and resolves them through an `IGlyphSource` (codepoint/glyph-id → glyph + advances + metrics) rather than a concrete `FontAtlas`, so the **shaping backend and the imagery backend swap independently**: a non-MTSDF imagery source (a bitmap atlas) can satisfy `IGlyphSource`, and a platform shaper can satisfy `ITextShaper`. The default `SimpleTextShaper` reproduces advance-based codepoint layout; `SUIsei.HarfBuzz` (a separate project, to hold the core's dependency floor) provides a HarfBuzzSharp shaper. The baker enumerates **GSUB-reachable** glyphs (a lookup walk over Single/Multiple/Alternate/Ligature/Reverse + unwrapped Extension), not just the `cmap` set — Cascadia's programming ligatures are `calt` contextual 1→1 substitutions reachable only through GSUB, so glyph *count* is preserved (caret/cluster mapping stays one glyph per source char) but the rendered glyph ids never appear in `cmap`.
- **Native MTSDF generation (`SUIsei.Text`).** A managed, multi-threaded MTSDF atlas generator with a hand-rolled OpenType reader and a hand-rolled, zero-dependency RGBA8 PNG encoder — no external font library, no `msdf-atlas-gen`. The distance math is a faithful port of msdfgen (`edgeColoringSimple`, the bisector-gated per-channel perpendicular-distance selector, the cubic solver) in `double` precision, with the winding-aware **overlapping-contour combiner** that was the single biggest correctness fix: without it a glyph built from overlapping contours (a stem plus separate arm rectangles) carves false notches through solid strokes. A final true-distance pass corrects residual channel-medial-axis edges. RGB carry the per-channel signed pseudo-distance; **A is the plain true signed distance** — the channel SUIsei reserves for outline / glow / weight. TrueType `glyf` is the verified outline path; CFF/Type2 (the OTF cubic format) is implemented behind the same `IGlyphOutlineSource` seam but **OTF-untested** (no `.otf` fixture is available).
- **The FontAtlas drop-in contract.** The emitted JSON + page is a drop-in for the runtime `FontAtlas`: `unitsPerEm` and metrics from `head` / `OS/2` typo fields; distance math in y-up EM space (TrueType's native winding gives inside-positive directly, with a nonzero-rule safety net flipping an inverted font); `planeBounds` is the ink box expanded by `pxRange/2` and y-flipped; `atlasBounds` is the integer texel rect with **no half-texel offset**, so a cell-pixel centre maps exactly onto a sampled texel centre under the UI shader's bilinear footprint; the page is decoded linear/UNORM, never sRGB. This is internally self-consistent generation↔sampling, and therefore *not* byte-identical to `msdf-atlas-gen` (whose `atlasBounds` carry a `+0.5` offset) — the bar is "visually indistinguishable / drop-in", not byte-identical.

### Pillar: headless-testable by construction
`UIHost.Tick(...)`, `UIRoot.LayoutPass(...)`, and `UIRoot.Render()` run to completion with **no engine, no backend, no window** — neutral events in, a neutral `Visual` / `DrawListData` tree out. Layout, input, routing, focus, animation, and draw-list build are asserted directly on CPU. `Dump()` / `DumpLayout()` / `DumpState()` snapshot surfaces make every stage inspectable. The current suite is ~225 tests across ~44 files with no GPU reference; this invariant is a design driver, not a later phase.

---

## 3. Load-bearing pillars (protect these)

- **Core depends on StbImageSharp only.** HarfBuzz and the MSDF generator stay satellites. Adding a runtime dependency to `SUIsei` core is a design violation.
- **Headless by construction** (above) — no engine handle in any logic type.
- **`IUICompositor` is a public portability boundary** — keep the `Visual` / `ContainerVisual` / `SpriteVisual` / `RootVisual` + content (`Rect` / `GlyphRun` / `Image`) surface small and dumpable; a foreign engine must be able to consume it without ever producing `DrawListData`. Content is retained and versioned, as WinUI's composition objects are: an element updates its content object in place, and a compositor reads `VisualContent.Version` to see what changed and to key the work it derives from it.
- **The `SUIsei.Drawing` ↔ host layout contract is byte-identical** — the zero-copy cast depends on it; change neither side without the other.
- **NativeAOT-clean** — typed DP delegates, generated `new`/setters, no `Activator`/`MethodInfo`/`Type`-keyed maps anywhere in the property / binding / template / resource path.
- **No allocations on the hot path.** `UIHost.Tick`/`Render`, routed-event dispatch, hit-testing, and the compositor's draw-list build allocate no managed memory on a steady-state frame — pooled route chains + args, retained/cached vertex buffers, the dirty-flag no-op short-circuit — especially in Release. Allocation belongs to construction and layout/structure changes, not the per-frame path.
- **DP precedence ladder:** `Animation > Local > Template > Style > Inherited > Default`, with the animation rung present from day one; a `{TemplateBinding}` onto a dependency property writes the template part's Template rung, so a Local value the app sets on the part wins over the template, as in WinUI; coercion applies to the **resolved** value (per read), not per-rung. (Dense `GlobalIndex` slots assigned via `[ModuleInitializer]`.) The value store is typed and boxing-free: a property's rungs live in a slot of its own value type, made on its first write to an object, so writes, coercion, change checks, change callbacks and `{TemplateBinding}` pushes never box. An animation writes a property every frame, so this is what keeps animating frames allocation-free. A DP is animatable when it has a `Lerp` (declared in its metadata, or standard for float, double, `Vector2`, `Vector3`, `Point` and `HdrColor`); storyboards reach it through a compile-time `PropertyPath`, and `AnimationClock.Tween` drives it, or any CLR property, from code.
- **Group opacity is true render-to-texture (Strategy A):** the GPU-free compositor emits zero-geometry `BeginLayer`/`EndLayer` markers; the host backend renders the subtree into an offscreen target and composites it at the group alpha. `SUIsei.Rasterizer` is the byte-identical CPU oracle.
- **Independent animation:** transform/opacity/brush animations write the `Visual` directly and recomposite the dirty subtree **without re-running layout or the element walk**.

---

## 4. The published XAML subset (a hard contract)

The generator errors clearly on anything outside this grammar; the subset is a published contract, not "best-effort WinUI." `XamlSubsetContractTests` holds it as a test: every **In** form must generate with no diagnostic and compile, and every **Planned** form must be reported.

**In:** elements/attributes; `x:Class` / `x:Name` / `x:Key` / `x:DataType`; property-element syntax for any settable property, collection or attached property (`<Border.Padding>4,8</Border.Padding>`, `<StackPanel.Children>`, `<Canvas.Left>`), holding text, one object, or a collection's items; content + attached properties; event handlers; markup extensions `{x:Bind}` / `{StaticResource}` / `{TemplateBinding}` / `{x:Null}` / `{x:Static}`; `<Style>` / `<Setter>` (+ `BasedOn`, `<Setter.Value>`, and attached-property setters such as `Property="Canvas.Left"`); `DataTemplate` / `ControlTemplate` / `ItemsPanelTemplate`; `ResourceDictionary` with inline `MergedDictionaries`; Grid row/column definitions; `mc:Ignorable` design-time markup (`d:`), which is skipped; `VisualStateManager` groups/states with `<Storyboard>`s of `DoubleAnimation` / `ColorAnimation` / `DoubleAnimationUsingKeyFrames` (`Linear` / `Discrete` / `EasingDoubleKeyFrame`) and `<X.EasingFunction>` elements (`CubicEase`, `QuadraticEase`, `BackEase`). `Storyboard.TargetProperty` takes WinUI property paths — `Opacity`, `(UIElement.Opacity)`, attached `(Canvas.Left)`, and `(Owner.Property).(Owner.Property)` steps through properties that hold objects — resolved to dependency properties at compile time; `Background` / `Foreground` / `Fill`, `(Border.Background).(SolidColorBrush.Color)` and `(UIElement.RenderTransform).(CompositeTransform.TranslateX)` (and `RenderTransform.Translate.X`) reach the element's colour and translation channels. `{x:Bind}` supports `OneTime` / `OneWay` / `TwoWay`, deep paths with INPC re-subscription, and `fallback` / `target-null`. `{StaticResource}` resolves while the tree is built, through the dictionaries of the element's markup ancestors (and their merged dictionaries), innermost first.

**XML namespaces.** The default namespace, `https://github.com/AtomicBlom/SUIsei/ui`, maps `SUIsei.Controls`, `SUIsei`, `SUIsei.Media`, `SUIsei.Shapes`, `SUIsei.Documents` and `SUIsei.Animation`, searched in that order, as WinUI's maps `Microsoft.UI.Xaml` and its sub-namespaces. `xmlns:local="using:My.Namespace"` maps one CLR namespace, for an app's own elements and for the owners `{x:Static}`, `x:DataType`, `TargetType` and property paths name. Any other namespace is foreign: its elements are unknown (SUISEI003) and its attributes are ignored with a warning (SUISEI013), unless `mc:Ignorable` lists its prefix.

**Values.** Text converts at compile time through one converter table keyed by the target type, shared by every value position: attributes, `<Setter Value>` and `<Setter.Value>`, attached properties, property-element text, animation `From` / `To` / `By` / `Duration` / `KeyTime` / `Value`, easing parameters, and `{x:Bind}`'s `FallbackValue` / `TargetNullValue`. It covers `HdrColor` (`#RRGGBB`, `#AARRGGBB` or a WinUI named colour such as `CornflowerBlue`), `Thickness` (one, two or four numbers), `Point` and `Vector2` (two numbers), `Vector3` (three), `GridLength` (`Auto`, `*`, `N*` or pixels), `TimeSpan`, `RepeatBehavior`, enums, numbers, bool and string; numbers are separated by commas or whitespace and must be finite. A value its type cannot hold is SUISEI012 at its XAML line. The colour and number syntax is one source file compiled into both the runtime (`HdrColor.Parse`) and the generator, so both accept the same text.

**Extensions (`suisei:` xmlns).** `xmlns:suisei="https://github.com/AtomicBlom/SUIsei/extensions"` maps `SUIsei.Extensions`: SUIsei-only types and attached properties with no WinUI counterpart, kept out of the default namespace so that WinUI markup stays WinUI and SUIsei's additions are visible as such. Its members are used like any other: `<suisei:Type>`, `suisei:Owner.Prop="…"`, `<suisei:Owner.Prop>`, `<Setter Property="suisei:Owner.Prop">` and the property path `(suisei:Owner.Prop)`. A name that does not resolve in it is SUISEI022. The rule covers SUIsei's own controls too: a public element member that WinUI does not have is an attached property here, not a member. Those today are `suisei:Clipping.ClipToBounds` (WinUI clips with `UIElement.Clip`, a geometry), `suisei:Scrolling.WheelScrollAmount` on a `ScrollViewer`, and `suisei:Virtualization.RowHeight` on a `VirtualizingStackPanel` (set on the panel inside an `ItemsPanelTemplate`, whose attributes are honoured like any template root's).

**Planned** (in the contract but not implemented; each is reported, never ignored): `{ThemeResource}` (SUISEI007 until the theme system exists); `ResourceDictionary Source="…"`, a dictionary loaded from another file (SUISEI020).

**Out (v1):** `{Binding}` (reflection-based); **`{x:Bind}` function paths and value converters** *(these were in the original plan but are deliberately excluded from the supported subset — they diagnose)*; `x:Phase`; `x:DeferLoadStrategy`; conditional XAML; custom markup extensions; `x:Uid` / localization markup; `RelativeSource`; implicit data templates by type.

Generated output is locked down by snapshot tests over representative `.xaml`; diagnostics carry the XAML `file:line` (22 distinct diagnostic codes today), and every malformed form is an error rather than a warning, except foreign or ignored content (SUISEI013) and a TwoWay binding downgraded to OneWay (SUISEI014). Incrementality keys on a `CompilationSnapshot` whose equality compares only the XAML input + a fingerprint of the type surface (each public or internal type of the current assembly and each public SUIsei type, with its members' accessibility, types, parameters and accessors), computed once per compilation, so body-only C# edits re-emit nothing while a changed member type regenerates; XAML Hot Reload is supported.

---

## 5. Current capabilities (the baseline)

Shipped and headlessly verified: the full DP / precedence / coercion / template-binding store; the element tree + two-pass measure/arrange (Grid pixel/auto/star + span, StackPanel, Canvas, Border, ContentPresenter/ItemsPresenter) with invalidation + frame-level no-op; a broad control set (TextBlock, Button, ToggleButton, CheckBox, Slider, ScrollViewer, TextBox, Popup, MenuFlyout(Item), MenuBar, ListView/ListViewItem, ItemsControl, Image); the composition tree + `DrawListCompositor` with damage tracking, incremental recomposition, and group-opacity layers; routed events (tunnel/bubble/direct, `Handled`, pooled chains, synthesized `Click`), hit-testing through the composition (below), focus + Tab (one tab order across views; `TabFocusNavigation` is not read), pointer capture/drag; `Control.IsEnabled` with WinUI's semantics (below); storyboard/keyframe/easing animation + `AnimationClock` + `VisualStateManager`; ItemsControl/ListView with `INotifyCollectionChanged` + `VirtualizingStackPanel`; the source generator (x:Bind OneTime/OneWay/TwoWay/fallback/deep paths/INPC, events, ControlTemplate/DataTemplate + item-scoped binds, TemplateBinding, Style/Setter/BasedOn, StaticResource/ResourceDictionary/merged, storyboard + VSM codegen); an MTSDF `FontAtlas` (codepoint + glyph-id) with the `ITextShaper` seam, a default advance-based shaper, a HarfBuzz satellite shaper, and a **native managed MTSDF generator + atlas baker + OpenType reader**; the HDR paper-white colour path; the `SUIsei.Rasterizer` CPU oracle; and the `SUIsei.Gallery` demo.

**Enabled state.** `IsEnabled` belongs to `Control`, as in WinUI; plain elements (Border, Panel, TextBlock, Image…) have none and leave input with `IsHitTestVisible`. It is inherited: while a control is disabled every control beneath it reads `IsEnabled == false` (a value set on it is kept for when the ancestor is enabled again), and `IsEnabledChanged` fires on each control whose effective value moves, including when one is attached under or detached from a disabled control. A disabled control takes no focus (Tab skips it, `TrySetFocus` refuses it, and the host releases focus from one disabled while focused before it routes the next keys), handles no pointer or keyboard input on itself, and shows its `CommonStates` `Disabled` state, or `Normal` where its template has no `Disabled`. Hit-testing follows WinUI's documented rule that a disabled control is "still visible to hit testing": it blocks pointer input to whatever lies behind it, and the input it sources still bubbles to its ancestors, which may handle it.

**Hit-testing.** A pointer hits what is drawn under it. The walk follows the composition the compositor draws: each element's visual transform is composed down from the root's DIP→pixel scale, so render transforms, animated translates, scroll offsets and `LayoutScale` all count, and overlays are tested above content. Clips are intersected as the compositor applies them, as surface-space bounding boxes; under rotation that box is larger than the rotated clip, and a point in its corners hits whatever is drawn there, so the pointer agrees with the pixels rather than with the ideal clip shape. An element is a hit only where its content draws, one case per content kind: a rect over its bounds, an image over its destination rect (not the letterbox), text over its layout bounds. `IsHitTestVisible = false` removes an element and its subtree. `UIElement.TransformToVisual` and `PointerRoutedEventArgs.GetCurrentPoint` map points through the same composed transforms, so controls never sum layout offsets themselves. Hit-testing and routing a pointer move allocate nothing.

**Diagnostics.** `SUIsei.Diagnostics` is what a diagnostics tool loaded into a running game builds on: the live hosts, the element tree, each dependency property's value with the level it came from (Animation, Local, Template, Style, Inherited or Default), writing and clearing the Local level, picking and bounds in surface pixels, a full-surface overlay kept above every popup for the tool's own UI, the generator's value syntaxes at run time (shared with it through `XamlValueSyntax`), each element's XAML file and position, and a hook for engine glue to supply the presented frame's pixels. Work reaches the UI thread through `Diagnostics.Post`, run at the start of the next `UIHost.Tick`. It is all behind the `SUIsei.Diagnostics.Enabled` feature switch, off unless a game sets it in its runtime config and always off under NativeAOT, so the trimmer removes it from a shipped game; the switch guards `RequiresDynamicCode` and `RequiresUnreferencedCode`, so code that loads an assembly behind it keeps the AOT analyzers quiet.

In-world surfaces, camera-ray hit-testing, and billboards are implemented in the **host glue** (`Loom.UI.SUIsei`), so SUIsei itself stays purely 2D and neutral (`UIHost.Tick` takes no camera; `CreateRoot` takes a 2D layer).

---

## 6. Remaining work

- **Rich text layout** — a `TextLayoutEngine` that itemizes (script/direction/font), shapes, line-breaks (UAX-14), and wraps; rich inline runs; a `FontFallbackChain` + `.notdef`/tofu glyph; build-time missing-glyph warnings. `TextBlock` is single-line advance-based today.
- **Cross-platform HarfBuzz** — the `ITextShaper` seam is wired (`FontAtlas` measure and run-build go through it) and the Gallery installs `HarfBuzzTextShaper`; what remains is shipping HarfBuzz native assets for Linux/macOS (Win32-only today). Platform shapers (a Windows `DWriteTextShaper`, a macOS Core Text one) are a clean drop-in at the same seam — capability-gated like a backend, not the default — and are deferred by decision; a full DWrite *bitmap* engine replacing MTSDF is explicitly rejected, as it would abandon resolution-independent, cross-platform-parity, AOT-clean text.
- **Native MSDF generation as the bake path** — `SUIsei.Text` / `SUIsei.Text.Tool` exist and replace the external `msdf-atlas-gen`, but are **not wired into the build**: add a `RegisterFonts` MSBuild target so fonts bake from a fontset at build time (the default atlas is currently a static embedded blob). Deferred within the generator: GPU-compute rasterization (the `IMsdfRasterizer` seam is ready for it), font hinting, variable-font axes, subset-`.ttf` emission for the shaper, and weight/script-aware font selection.
- **Gamepad + XYFocus spatial navigation** — the input enums exist; the directional scoring heuristic (with tunable weights + a debug overlay) is unimplemented, and there is no `GamepadInput` queue yet.
- **IME** — committed-character text input works; add an `ITextInputProvider` host seam so per-OS IME lands without reworking `TextBox`.
- **NativeAOT CI gate** — the libraries set `IsAotCompatible`, but there is no `PublishAot` smoke-publish (warnings-as-errors) gate in CI.
- **Deferred:** multi-screen hosts, in-world occlusion (the analytic ray test ignores scene depth; opt-in depth-sampled occlusion is a later escape hatch), above-paper-white roll-off (hard clamp vs soft knee), and compositor threading.

---

## 7. Open risks

- **Rich-text / shaping is the biggest remaining greenfield** — mitigated by prebake + fallback + tofu + build-time warnings, with HarfBuzz as the proven shaper behind the swappable seam.
- **The XAML subset must stay a published hard contract** — and the contract must be reconciled with what shipped (function paths and converters were dropped from the supported `{x:Bind}` subset; §4 reflects the real state, and `XamlSubsetContractTests` fails when the two drift apart).
- **Composition-tree sync** — the element tree must push layout/property deltas into the visual tree and keep damage flags honest, or incremental recomposition drifts. Mitigated by making both trees dumpable and asserting *element → visual → draw-data* as separate pure stages.
- **The `SUIsei.Drawing` ↔ host byte-identical layout contract is load-bearing and unguarded** — a layout drift on either side silently corrupts the zero-copy cast; an analyzer or a layout-assertion test would harden it.
- **XYFocus heuristic** needs tuning (WinUI's constants are private) — ship tunable weights + a debug overlay.

---

## 8. Verification

The strategy is **graphics-framework-free by design** — almost the whole runtime is exercised by fast, deterministic CPU tests; only the host backend's *pixels* need a GPU:

- **Unit tests** (`SUIsei.Tests`, TUnit + Shouldly, no backend reference): DP precedence (incl. the animation rung); Grid star/auto/measure-arrange; the neutral draw-list seam (assert the `DrawListData` vertex/command stream); 2D + ray→UV hit-testing; routed-event tunnel/bubble ordering + `Handled`; storyboard interpolation; the `SUIsei.Rasterizer` group-opacity oracle; text against a committed font fixture.
- **Source-generator snapshot tests** over representative `.xaml`, with diagnostics carrying the XAML `file:line`.
- **Host-side pixels only** — the integrating engine captures a deterministic frame and pixel-diffs across its backends (in Loom: Vulkan vs DirectX 12). Everything above the final draw-list submit is covered by the CPU tests.

---

*Note on naming and comments: per the consuming engine's convention, source comments must be self-contained — no milestone (`MUx`), planning-section (`§Dx`), or `Planning.md`-reference tags. The historical `MU0…MU5` / `PlanningUI.md §…` tags scattered through the source are being stripped; do not add new ones, and do not reference this document by section number from code.*
