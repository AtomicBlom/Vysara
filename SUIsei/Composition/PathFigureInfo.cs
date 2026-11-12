namespace SUIsei.Composition;

/// <summary>
/// Where one figure of a <see cref="CompositionPath"/> lies in its <see cref="CompositionPath.Verbs"/> and
/// <see cref="CompositionPath.Points"/>, starting with its <see cref="PathVerb.Move"/>.
/// </summary>
/// <param name="FirstVerb">The index of the figure's move.</param>
/// <param name="VerbCount">The figure's verbs, its move and any close included.</param>
/// <param name="FirstPoint">The index of the figure's start point.</param>
/// <param name="PointCount">The figure's points, its start point included.</param>
/// <param name="IsClosed">Whether the figure ends with <see cref="PathVerb.Close"/>.</param>
/// <param name="IsFilled">Whether the figure's interior takes part in a fill, as WinUI's <c>PathFigure.IsFilled</c>.</param>
public readonly record struct PathFigureInfo(int FirstVerb, int VerbCount, int FirstPoint, int PointCount, bool IsClosed, bool IsFilled);
