namespace Vysara.Markup;

/// <summary>
/// What <see cref="PathMarkup.TryParse"/> reports as it reads WinUI path markup: the fill rule, then each figure as a
/// start point and drawing commands, all in absolute coordinates. Relative commands, <c>H</c>/<c>V</c> and the smooth
/// <c>S</c>/<c>T</c> forms arrive already resolved to the absolute line and curve they draw.
/// </summary>
internal interface IPathMarkupSink
{
	/// <summary>The leading <c>F0</c> (even-odd, false) or <c>F1</c> (nonzero, true); not called when the markup has none.</summary>
	void SetFillRule(bool nonzero);

	void BeginFigure(double x, double y);

	void LineTo(double x, double y);

	void QuadraticTo(double x1, double y1, double x, double y);

	void CubicTo(double x1, double y1, double x2, double y2, double x, double y);

	/// <summary>An elliptical arc to (<paramref name="x"/>, <paramref name="y"/>), in the endpoint form of
	/// WinUI's <c>ArcSegment</c>: radii, the ellipse's rotation in degrees, and the two flags.</summary>
	void ArcTo(double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool clockwise, double x, double y);

	void CloseFigure();
}
