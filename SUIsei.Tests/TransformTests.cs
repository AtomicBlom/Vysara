using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// The transform family, as WinUI's: each kind maps points as WinUI documents it, a render transform applies about the
/// element's origin with no effect on layout, a change to a transform (shared or in a group) moves its elements on the
/// visual path, and a storyboard animates a transform's properties through a WinUI path.
/// </summary>
public sealed class TransformTests
{
	private static Point Map(Transform transform, float x, float y) => transform.TransformPoint(new Point(x, y));

	private static void ShouldBeNear(Point actual, float x, float y)
	{
		actual.X.ShouldBe(x, 1e-4f);
		actual.Y.ShouldBe(y, 1e-4f);
	}

	[Test]
	public void Each_transform_maps_points_as_WinUI_does()
	{
		ShouldBeNear(Map(new TranslateTransform { X = 5f, Y = -2f }, 1f, 1f), 6f, -1f);
		ShouldBeNear(Map(new ScaleTransform { ScaleX = 2f, ScaleY = 3f, CenterX = 10f, CenterY = 10f }, 12f, 11f), 14f, 13f);
		// Positive angles turn clockwise on screen (y down): +x goes to +y.
		ShouldBeNear(Map(new RotateTransform { Angle = 90f }, 1f, 0f), 0f, 1f);
		ShouldBeNear(Map(new RotateTransform { Angle = 90f, CenterX = 10f, CenterY = 10f }, 11f, 10f), 10f, 11f);
		// x moves by y·tan(AngleX), y by x·tan(AngleY).
		ShouldBeNear(Map(new SkewTransform { AngleX = 45f }, 0f, 2f), 2f, 2f);
		ShouldBeNear(Map(new SkewTransform { AngleY = 45f, CenterX = 1f }, 3f, 0f), 3f, 2f);
		ShouldBeNear(Map(new MatrixTransform { Matrix = new Matrix3x2(1f, 0f, 0f, 1f, 4f, 5f) }, 1f, 1f), 5f, 6f);
	}

	[Test]
	public void A_composite_transform_scales_skews_rotates_and_then_translates()
	{
		var composite = new CompositeTransform { ScaleX = 2f, Rotation = 90f, TranslateX = 100f, CenterX = 1f };
		// About x = 1: (2, 0) scales to (3, 0), rotates to (1, 2), then moves by 100.
		ShouldBeNear(Map(composite, 2f, 0f), 101f, 2f);

		var group = new TransformGroup { Children = { new ScaleTransform { ScaleX = 2f, CenterX = 1f }, new RotateTransform { Angle = 90f, CenterX = 1f }, new TranslateTransform { X = 100f } } };
		ShouldBeNear(Map(group, 2f, 0f), 101f, 2f);
		var viaValue = Vector2.Transform(new Vector2(2f, 0f), group.Value);
		ShouldBeNear(new Point(viaValue.X, viaValue.Y), 101f, 2f);
	}

	[Test]
	public void A_transform_is_a_general_transform_with_an_inverse()
	{
		var rotate = new RotateTransform { Angle = 30f, CenterX = 4f, CenterY = 2f };
		var there = rotate.TransformPoint(new Point(7f, 9f));
		ShouldBeNear(rotate.Inverse!.TransformPoint(there), 7f, 9f);
		rotate.TransformBounds(new Rect(0f, 0f, 10f, 0f)).Width.ShouldBe(10f * MathF.Cos(MathF.PI / 6f), 1e-4f);
		new ScaleTransform { ScaleX = 0f }.Inverse.ShouldBeNull();
	}

	[Test]
	public void A_render_transform_applies_about_its_origin_with_no_effect_on_layout()
	{
		var border = new Border { Width = 100f, Height = 50f, Background = new SolidColorBrush(HdrColor.FromRgb(1, 2, 3)) };
		var canvas = new Canvas { Width = 400f, Height = 400f };
		canvas.Children.Add(border);
		Canvas.SetLeft(border, 10f);
		var root = new UIRoot(new DrawListId(0)) { Content = canvas };
		root.LayoutPass(new Size(400f, 400f));

		border.RenderTransform = new RotateTransform { Angle = 180f };
		border.RenderTransformOrigin = new Point(0.5f, 0.5f);

		border.MeasureDirty.ShouldBeFalse();
		border.ArrangeDirty.ShouldBeFalse();
		border.LayoutRect.ShouldBe(new Rect(10f, 0f, 100f, 50f));
		// Turned half a turn about its centre: its top-left corner lands at its bottom-right.
		ShouldBeNear(border.TransformToVisual(null).TransformPoint(new Point(0f, 0f)), 110f, 50f);
	}

	[Test]
	public void A_change_to_a_shared_transform_or_a_group_child_moves_its_elements_on_the_visual_path()
	{
		var shift = new TranslateTransform();
		var first = new Border { Width = 10f, Height = 10f, RenderTransform = shift };
		var second = new Border { Width = 10f, Height = 10f, RenderTransform = new TransformGroup { Children = { new ScaleTransform(), shift } } };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		root.LayoutPass(new Size(100f, 100f));
		root.Render();
		var firstX = first.Visual.Transform.M31;
		var secondX = second.Visual.Transform.M31;

		shift.X = 7f;

		first.Visual.Transform.M31.ShouldBe(firstX + 7f);
		second.Visual.Transform.M31.ShouldBe(secondX + 7f);
		first.Visual.IsDirty.ShouldBeTrue();
		first.ArrangeDirty.ShouldBeFalse();
		second.ArrangeDirty.ShouldBeFalse();

		((ScaleTransform)((TransformGroup)second.RenderTransform!).Children[0]).ScaleX = 2f;
		second.Visual.Transform.M11.ShouldBe(2f);

		// Elements stop listening when they leave the tree, as they do for brushes.
		shift.ListenerCount.ShouldBe(2); // first, and the group (which second listens to)
		root.Content = null;
		shift.ListenerCount.ShouldBe(1); // only the group, its child for as long as it holds it
		((TransformGroup)second.RenderTransform!).ListenerCount.ShouldBe(0);
	}

	[Test]
	public void A_storyboard_animates_a_transform_through_a_WinUI_path()
	{
		var composite = new CompositeTransform();
		var border = new Border { Width = 10f, Height = 10f, RenderTransform = composite };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = border;
		host.Tick(0, 0f, new Vector2(50f, 50f));

		host.Clock.Begin(new Storyboard
		{
			Children =
			{
				new DoubleAnimation
				{
					To = 90.0, Duration = TimeSpan.FromSeconds(1), Target = border,
					TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, CompositeTransform.RotationProperty),
				},
			},
		});
		host.Tick(0.5, 0f, new Vector2(50f, 50f));

		composite.Rotation.ShouldBe(45f, 1e-4f);
		border.Visual.Transform.M12.ShouldBe(MathF.Sin(MathF.PI / 4f), 1e-5f);
		CompositeTransform.RotationProperty.IsAnimatable.ShouldBeTrue();
		MatrixTransform.MatrixProperty.IsAnimatable.ShouldBeFalse();
	}

	[Test]
	public void A_path_into_a_transform_of_another_kind_fails_when_it_begins()
	{
		var border = new Border { RenderTransform = new ScaleTransform() };
		var storyboard = new Storyboard
		{
			Children = { new DoubleAnimation { To = 1.0, Target = border, TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, TranslateTransform.XProperty) } },
		};

		Should.Throw<InvalidOperationException>(() => new AnimationClock().Begin(storyboard)).Message.ShouldContain("RenderTransform holds a ScaleTransform");
	}

	[Test]
	public void TransformToVisual_returns_a_matrix_transform()
	{
		var border = new Border { Width = 10f, Height = 10f, Translation = new Vector3(3f, 4f, 0f) };
		var root = new UIRoot(new DrawListId(0)) { Content = border };
		root.LayoutPass(new Size(10f, 10f));

		var toRoot = border.TransformToVisual(null).ShouldBeOfType<MatrixTransform>();
		toRoot.Matrix.ShouldBe(Matrix3x2.CreateTranslation(3f, 4f));
	}
}
