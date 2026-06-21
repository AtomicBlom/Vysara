namespace SUIsei.Controls;

/// <summary>
/// Builds the <see cref="Panel"/> an <see cref="ItemsControl"/> lays its item containers out in (PlanningUI.md
/// §D1.6). The default is a vertical <see cref="StackPanel"/>; a virtualizing list supplies a
/// <see cref="VirtualizingStackPanel"/> instead.
/// </summary>
public sealed class ItemsPanelTemplate
{
	/// <summary>Creates a fresh items-host panel.</summary>
	public required Func<Panel> Factory { get; init; }

	/// <summary>The default vertical <see cref="StackPanel"/> host.</summary>
	public static ItemsPanelTemplate DefaultStackPanel { get; } = new() { Factory = static () => new StackPanel() };

	/// <summary>Build a new panel instance.</summary>
	public Panel Build() => Factory();
}
