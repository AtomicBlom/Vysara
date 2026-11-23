using System.Numerics;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Template runtime coverage: a <see cref="ControlTemplate"/> realized on first measure becomes
/// the control's single visual child; <c>{TemplateBinding Content}</c> routes the control's content into the
/// template's <see cref="ContentPresenter"/> (live, through <see cref="ContentControl.RegisterContentTemplateBinding"/>);
/// <see cref="ContentPresenter.ContentTemplate"/> realizes a <see cref="DataTemplate"/>; and
/// <see cref="DependencyObject.RegisterTemplateBinding"/> pushes a DP-valued binding. All headless.
/// </summary>
public sealed class TemplateTests
{
	private static ControlTemplate BorderWithPresenter() => new()
	{
		Factory = control =>
		{
			var cc = (ContentControl)control;
			var border = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(10, 20, 30)), CornerRadius = new CornerRadius(4f) };
			var presenter = new ContentPresenter();
			border.Child = presenter;
			cc.RegisterContentTemplateBinding(content => presenter.Content = content); // {TemplateBinding Content}
			return border;
		},
	};

	private static (ContentControl Control, ContentPresenter Presenter) TemplatedControl(string content)
	{
		var control = new ContentControl { Template = BorderWithPresenter(), Content = content, Width = 100, Height = 40 };
		var root = new UIRoot(new DrawListId(0)) { Content = control };
		root.LayoutPass(new Size(200, 200));
		var border = (Border)control.GetTemplateRoot()!;
		return (control, (ContentPresenter)border.Child!);
	}

	[Test]
	public void A_control_template_becomes_the_controls_visual_child_and_lays_out()
	{
		var (control, _) = TemplatedControl("Hi");

		control.GetTemplateRoot().ShouldBeOfType<Border>();
		control.VisualChildren.Count.ShouldBe(1);
		control.VisualChildren[0].ShouldBe(control.GetTemplateRoot());
		control.GetTemplateRoot()!.LayoutRect.Width.ShouldBe(100f); // template filled the templated control
	}

	[Test]
	public void Template_binding_content_shows_and_stays_live()
	{
		var (control, presenter) = TemplatedControl("Hi");

		((TextBlock)presenter.VisualChildren[0]).Text.ShouldBe("Hi");

		control.Content = "Bye";
		((TextBlock)presenter.VisualChildren[0]).Text.ShouldBe("Bye"); // the content template binding kept the presenter in sync
	}

	[Test]
	public void Content_presenter_realizes_a_data_template()
	{
		var dt = new DataTemplate { Factory = item => new TextBlock { Text = $"[{item}]" } };
		var presenter = new ContentPresenter { ContentTemplate = dt, Content = "x" };

		((TextBlock)presenter.VisualChildren[0]).Text.ShouldBe("[x]");
	}

	[Test]
	public void Register_template_binding_pushes_initial_and_live_values()
	{
		var source = new ContentControl();
		var pushed = -1f;
		source.RegisterTemplateBinding(UIElement.OpacityProperty, v => pushed = (float)v!);

		pushed.ShouldBe(1f); // initial (default opacity)
		source.Opacity = 0.3f;
		pushed.ShouldBe(0.3f); // live
	}

	[Test]
	public void A_template_binding_to_a_dependency_property_writes_the_parts_template_rung()
	{
		var control = new ContentControl { Opacity = 0.5f };
		var part = new Border();
		control.RegisterTemplateBinding(UIElement.OpacityProperty, part, UIElement.OpacityProperty);

		part.Opacity.ShouldBe(0.5f);
		part.HasLocalValue(UIElement.OpacityProperty).ShouldBeFalse(); // the template's value, not the part's own
		control.Opacity = 0.25f;
		part.Opacity.ShouldBe(0.25f); // live
	}

	[Test]
	public void A_local_value_on_a_template_part_wins_over_the_template_and_survives_its_next_push()
	{
		var control = new ContentControl { Opacity = 0.5f };
		var part = new Border();
		control.RegisterTemplateBinding(UIElement.OpacityProperty, part, UIElement.OpacityProperty);

		part.Opacity = 0.8f;
		part.Opacity.ShouldBe(0.8f);
		control.Opacity = 0.2f;
		part.Opacity.ShouldBe(0.8f); // the push lands below Local, as in WinUI

		UIElement.OpacityProperty.ClearRung(part, DependencyObject.ValueRung.Local);
		part.Opacity.ShouldBe(0.2f); // with Local cleared, the template's latest value shows
	}

	[Test]
	public void A_converting_template_binding_writes_the_converted_value()
	{
		var slider = new Slider { Maximum = 100, Value = 12 };
		var part = new TextBlock();
		slider.RegisterTemplateBinding(Slider.ValueProperty, part, TextBlock.FontSizeProperty, static v => (float)v);

		part.FontSize.ShouldBe(12f);
		slider.Value = 30;
		part.FontSize.ShouldBe(30f);
		part.HasLocalValue(TextBlock.FontSizeProperty).ShouldBeFalse();
	}

	[Test]
	public void Button_hover_animates_its_background_through_the_visual_state_manager()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var button = new Button { Content = "Go", Width = 40, Height = 20 };
		view.Root.Content = button;
		host.Tick(0.016, 0f, new Vector2(100, 100));

		VisualStateManager.GoToState(button, "PointerOver", useTransitions: true);
		host.Tick(0.2, 0f, new Vector2(100, 100)); // run the 120ms transition to completion

		// The button is now templated, so the animated fill lives on the template root (the Border), not the button.
		var border = (Border)button.GetTemplateRoot()!;
		var content = border.Visual.Content.ShouldBeOfType<RectContent>();
		content.Fill.SolidColor().ShouldBe(HdrColor.FromRgb(80, 80, 95)); // the PointerOver background
	}

	[Test]
	public void Pressing_a_templated_buttons_inner_part_focuses_the_button()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var button = new Button { Content = "Go", Width = 60, Height = 24 };
		view.Root.Content = button;
		host.Tick(0.016, 0f, new Vector2(200, 200)); // lay out → realize the template

		// The hit target inside a templated button is its non-focusable template root; focusing from there must walk
		// up to the focusable Button (the regression the focus-ancestor-walk fixes).
		var templateRoot = button.GetTemplateRoot()!;
		templateRoot.IsTabStop.ShouldBeFalse();
		host.Focus.FocusFromPointer(templateRoot);
		host.Focus.FocusedElement.ShouldBe(button);
	}
}
