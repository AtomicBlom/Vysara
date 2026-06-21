using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// Template runtime coverage (PlanningUI.md §D1.6): a <see cref="ControlTemplate"/> realized on first measure becomes
/// the control's single visual child; <c>{TemplateBinding Content}</c> routes the control's content into the
/// template's <see cref="ContentPresenter"/> (live via <see cref="ContentControl.ContentChanged"/>);
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
			var border = new Border { Background = HdrColor.FromRgb(10, 20, 30), CornerRadius = 4f };
			var presenter = new ContentPresenter();
			border.Child = presenter;
			presenter.Content = cc.Content; // {TemplateBinding Content} — initial
			cc.ContentChanged += (_, _) => presenter.Content = cc.Content; // live
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
		((TextBlock)presenter.VisualChildren[0]).Text.ShouldBe("Bye"); // ContentChanged kept the presenter in sync
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
	public void Button_hover_animates_its_background_through_the_visual_state_manager()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UIPlacement.Overlay(0, 1f));
		var button = new Button { Content = "Go", Width = 40, Height = 20 };
		view.Root.Content = button;
		host.Tick(0.016, 0f, new Vector2(100, 100));

		VisualStateManager.GoToState(button, "PointerOver", useTransitions: true);
		host.Tick(0.2, 0f, new Vector2(100, 100)); // run the 120ms transition to completion

		var content = button.Visual.Content.ShouldBeOfType<RectContent>();
		content.Fill.ShouldBe(HdrColor.FromRgb(80, 80, 95)); // the PointerOver background
	}
}
