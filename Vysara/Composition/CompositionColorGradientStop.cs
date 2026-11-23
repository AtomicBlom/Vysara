namespace Vysara.Composition;

/// <summary>One colour of a <see cref="CompositionGradientBrush"/>, at <see cref="Offset"/> along the gradient (0 at its
/// start, 1 at its end).</summary>
public readonly record struct CompositionColorGradientStop(float Offset, HdrColor Color);
