namespace SUIsei.Controls;

/// <summary>
/// The base of the compile-time templates (PlanningUI.md §D1.6). A template is a generated factory that <c>new</c>s a
/// subtree — never reflection — so <see cref="ControlTemplate"/> and <see cref="DataTemplate"/> differ only in what
/// the factory is handed (the templated control vs the data item).
/// </summary>
public abstract class FrameworkTemplate;
