# SUIsei

A retained-mode, WinUI-inspired UI framework for .NET 10, designed for NativeAOT compatibility and headless testability.

## Features

- **Retained-mode control tree** — `UIElement` / `FrameworkElement` / `Control` hierarchy with dependency properties, routed events, and data binding
- **XAML source generator** — `SUIsei.SourceGen` compiles `.xaml` files at build time (bundled in the NuGet package)
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
