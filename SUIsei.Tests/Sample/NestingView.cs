using SUIsei.Controls;

namespace SUIsei.Tests.Sample;

/// <summary>
/// The hand-authored half of <c>Sample/NestingView.xaml</c>: the bind root, and what <see cref="OnComponentRebuilt"/>
/// found when it ran, which says whether the rebuilt tree, its bindings and its new root were in place by then.
/// </summary>
internal sealed partial class NestingView
{
	public BindingViewModel ViewModel { get; } = new();

	/// <summary>How many times <see cref="OnComponentRebuilt"/> has run.</summary>
	public int Rebuilds;

	/// <summary><c>Before</c> and its bound text, when the hook ran.</summary>
	public (TextBlock Element, string Text)? BeforeAtRebuild;

	/// <summary>The root and its parent, when the hook ran.</summary>
	public (StackPanel Element, UIElement? Parent)? RootAtRebuild;

	public NestingView() => InitializeComponent();

	partial void OnComponentRebuilt()
	{
		Rebuilds++;
		BeforeAtRebuild = (Before, Before.Text);
		RootAtRebuild = (Root, Root.Parent);
	}
}
