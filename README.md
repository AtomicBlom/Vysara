# SUIsei

A retained-mode, WinUI-inspired UI framework for .NET 11, designed for NativeAOT compatibility and headless testability.

## Features

- **Retained-mode control tree** — `UIElement` / `FrameworkElement` / `Control` hierarchy with dependency properties, routed events, and data binding
- **XAML source generator** — `SUIsei.SourceGen` compiles `.xaml` files at build time (bundled in the NuGet package) into typed construction code, converting values and resolving types, bindings and property paths at compile time. It accepts a published WinUI-dialect subset (Planning.md §4); SUIsei-only types and attached properties live in the `suisei:` extension namespace (`https://github.com/AtomicBlom/SUIsei/extensions`)
- **Brushes and transforms, as WinUI's** — `SolidColorBrush`, `LinearGradientBrush` and `ImageBrush` for `Background` / `BorderBrush` / `Foreground`, shareable from a `ResourceDictionary` and animatable through paths such as `(Border.Background).(SolidColorBrush.Color)`; the `RenderTransform` family (`Translate`, `Scale`, `Rotate`, `Skew`, `Composite`, `Matrix`, `TransformGroup`) and WinUI 3's `UIElement.Translation`. Gradients are drawn exactly, colours above paper white included, with no ramp texture
- **Images, as WinUI's** — `BitmapSource` (decoded with StbImageSharp) and `WriteableBitmap` (rewritten through `PixelBuffer` and `Invalidate()`, every frame if need be, without allocating), plus SUIsei's `HostImageSource` for a texture the host supplies, such as a render target. Each image declares straight or premultiplied alpha and sRGB or linear colour, so translucent images blend correctly in linear light, and the host is told when nothing draws an image any more, or it is disposed, so it can release the texture
- **Compositor** — walks the visual tree and produces a backend-neutral `DrawListData` (vertices + commands) with incremental damage tracking
- **Vector geometry** — WinUI's `Geometry` family (with `Geometry.Transform`) and path markup, and a public tessellator that turns paths into analytically antialiased triangles (WinUI's joins, caps, dashes and fill rules), drawn by the `Vector` primitive from retained shape content (`ShapeContent`: a fill and a stroke painted by colour, exact gradient or image, its tessellation cached) and trimmed at draw time without re-tessellating
- **Shapes, as WinUI's** — `Rectangle`, `Ellipse`, `Line`, `Polyline`, `Polygon` and `Path` with the full stroke set (dashes, joins, miter limit, caps) and `Stretch` layout, hit-tested on what they draw, and animatable without layout or re-tessellation; drawn on with `suisei:StrokeTrim` and coloured along their length with `suisei:StrokeBrushMapping`
- **MTSDF text** — glyph atlases baked by a native, dependency-free generator (`SUIsei.Text`, the `loom-msdf` tool) whose field is checked against each glyph's exact fill and against small text drawn at every sub-pixel phase, a default atlas covering Latin-1 and the common UI symbols, its distance range leaving room for the outlines and soft shadows `suisei:TextEffects` draws from the field, sized in EM and animatable without layout or allocation; shaped behind the swappable `ITextShaper` (advance-based by default, HarfBuzz as a satellite) with OpenType features, so WinUI's `Typography.NumeralAlignment="Tabular"` lines numbers up; `FontSize` and `Foreground` inherited down the tree from the control or presenter above, as WinUI's are, and each line placed by WinUI's `TextAlignment`; text measures and draws with one font, its root's, and a shaped run is cached on its content, so recompositing never re-shapes
- **Headless by design** — no GPU dependency; the full layout + input + composite pipeline is unit-testable without a window
- **Animation** — storyboard-based animations driven by `AnimationClock`, targeting WinUI property paths such as `(Canvas.Left)`, with keyframes, WinUI's easing functions, and `ObjectAnimationUsingKeyFrames` to set any property (collapse what a state fades out, show a brush or text); allocation-free code-first `AnimationClock.Tween`; `VisualStateManager` for control state transitions
- **NativeAOT safe** — no reflection at runtime; all code paths are trim-friendly

## Usage

```xml
<PackageReference Include="SUIsei" Version="0.1.0-preview.1" />
```

XAML files are automatically picked up as `AdditionalFiles` by the bundled `SUIsei.SourceGen` source generator — no extra configuration needed.

## The gallery

`SUIsei.Gallery` shows SUIsei's features a page each, drawn by the CPU rasterizer into a Win32 window with no GPU:

```
dotnet run --project SUIsei.Gallery -c Release                          # the window, at its first page
dotnet run --project SUIsei.Gallery -c Release -- --page controls       # the window, at a page
dotnet run --project SUIsei.Gallery -c Release -- --capture-pages out   # every page as a PNG, as the tests draw it
dotnet run --project SUIsei.Gallery -c Release -- --capture shell.png --page layout
```

The pages live in `SUIsei.Gallery.Pages`, one XAML file each, and reload under `dotnet watch run`. `SUIsei.Tests` draws each
page and the shell and compares them with their golden images in `SUIsei.Tests/Goldens/Gallery/`; after changing a page
on purpose, write its golden again with `SUISEI_GOLDEN_UPDATE=1 dotnet run --project SUIsei.Tests -c Release` and look at
the new image before committing it.

## Integration with Loom

Use `Loom.UI.SUIsei` (from the [Loom](https://github.com/AtomicBlom/Loom) package) to render SUIsei draw lists through the Loom render graph.

## Links

- [GitHub](https://github.com/AtomicBlom/SUIsei)
