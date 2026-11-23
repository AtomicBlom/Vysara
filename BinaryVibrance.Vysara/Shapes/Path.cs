using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Shapes;

/// <summary>
/// Draws a <see cref="Geometry"/>, as WinUI's <c>Path</c>: <see cref="Data"/>, through its
/// <see cref="Geometry.Transform"/>. In XAML <c>Data</c> is often path markup, <c>Data="M 0,0 L 10,10 20,0 Z"</c>. A
/// change anywhere in the geometry, down to one point, applies in place, as does one to its transform.
/// </summary>
public sealed class Path : Shape
{
	public static readonly DependencyProperty<Path, Geometry?> DataProperty =
		DependencyProperty.Register(nameof(Data), new PropertyMetadata<Path, Geometry?>
		{
			PropertyChanged = static (path, oldValue, newValue) =>
			{
				if (path.AttachedRoot is not null)
				{
					path.Listen(oldValue, false);
					path.Listen(newValue, true);
				}
				path.OnOutlineChanged();
			},
		});

	/// <summary>The geometry drawn, in the shape's own coordinates; null draws nothing.</summary>
	public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

	private protected override CompositionPath? DefiningPath => Data?.CompositionPath;

	private protected override Matrix3x2 DefiningTransform => Data?.TransformMatrix ?? Matrix3x2.Identity;

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Data, attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override void OnSubPropertyChanged(object source)
	{
		if (ReferenceEquals(source, Data))
			OnOutlineChanged();
		else
			base.OnSubPropertyChanged(source);
	}
}
