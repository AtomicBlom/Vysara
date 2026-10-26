# SUIsei

A retained-mode, WinUI-inspired UI framework for .NET 10, designed for NativeAOT compatibility and headless testability.

## Features

- **Retained-mode control tree** — `UIElement` / `FrameworkElement` / `Control` hierarchy with dependency properties, routed events, and data binding
- **XAML source generator** — `SUIsei.SourceGen` compiles `.xaml` files at build time (bundled in the NuGet package) into typed construction code, converting values and resolving types, bindings and property paths at compile time. It accepts a published WinUI-dialect subset (Planning.md §4); SUIsei-only types and attached properties live in the `suisei:` extension namespace (`https://github.com/AtomicBlom/SUIsei/extensions`)
- **Brushes and transforms, as WinUI's** — `SolidColorBrush`, `LinearGradientBrush` and `ImageBrush` for `Background` / `BorderBrush` / `Foreground`, shareable from a `ResourceDictionary` and animatable through paths such as `(Border.Background).(SolidColorBrush.Color)`; the `RenderTransform` family (`Translate`, `Scale`, `Rotate`, `Skew`, `Composite`, `Matrix`, `TransformGroup`) and WinUI 3's `UIElement.Translation`. Gradients are drawn exactly, colours above paper white included, with no ramp texture
- **Compositor** — walks the visual tree and produces a backend-neutral `DrawListData` (vertices + commands) with incremental damage tracking
- **Headless by design** — no GPU dependency; the full layout + input + composite pipeline is unit-testable without a window
- **Animation** — storyboard-based animations driven by `AnimationClock`, targeting WinUI property paths such as `(Canvas.Left)`, with keyframes and easing; allocation-free code-first `AnimationClock.Tween`; `VisualStateManager` for control state transitions
- **NativeAOT safe** — no reflection at runtime; all code paths are trim-friendly

## Usage

```xml
<PackageReference Include="SUIsei" Version="0.1.0-preview.1" />
```

XAML files are automatically picked up as `AdditionalFiles` by the bundled `SUIsei.SourceGen` source generator — no extra configuration needed.

## Integration with Loom

Use `Loom.UI.SUIsei` (from the [Loom](https://github.com/AtomicBlom/Loom) package) to render SUIsei draw lists through the Loom render graph.

## Links

- [GitHub](https://github.com/AtomicBlom/SUIsei)
