using System.Numerics;
using Vysara.Composition;

namespace Vysara.Shapes;

/// <summary>A straight line from (<see cref="X1"/>, <see cref="Y1"/>) to (<see cref="X2"/>, <see cref="Y2"/>), as
/// WinUI's <c>Line</c>. It encloses nothing, so it is only stroked.</summary>
public sealed class Line : Shape
{
	public static readonly DependencyProperty<Line, float> X1Property = RegisterEnd(nameof(X1));
	public static readonly DependencyProperty<Line, float> Y1Property = RegisterEnd(nameof(Y1));
	public static readonly DependencyProperty<Line, float> X2Property = RegisterEnd(nameof(X2));
	public static readonly DependencyProperty<Line, float> Y2Property = RegisterEnd(nameof(Y2));

	private readonly CompositionPath _path = new();
	private bool _current;

	/// <summary>Where the line starts, along x, in the shape's own coordinates.</summary>
	public float X1 { get => GetValue(X1Property); set => SetValue(X1Property, value); }

	/// <summary>Where the line starts, along y.</summary>
	public float Y1 { get => GetValue(Y1Property); set => SetValue(Y1Property, value); }

	/// <summary>Where the line ends, along x.</summary>
	public float X2 { get => GetValue(X2Property); set => SetValue(X2Property, value); }

	/// <summary>Where the line ends, along y.</summary>
	public float Y2 { get => GetValue(Y2Property); set => SetValue(Y2Property, value); }

	private protected override CompositionPath DefiningPath
	{
		get
		{
			if (!_current)
			{
				_path.Clear();
				_path.MoveTo(new Vector2(X1, Y1), filled: false);
				_path.LineTo(new Vector2(X2, Y2));
				_current = true;
			}
			return _path;
		}
	}

	private static DependencyProperty<Line, float> RegisterEnd(string name) =>
		DependencyProperty.Register(name, new PropertyMetadata<Line, float>
		{
			PropertyChanged = static (line, _, _) =>
			{
				line._current = false;
				line.OnOutlineChanged();
			},
		});
}
