namespace SUIsei;

/// <summary>
/// The single routed-event delegate shape (PlanningUI.md §D5.3) — typed, so invocation is a direct call with no
/// <c>MethodInfo.Invoke</c> and no argument boxing (the NAOT requirement). The generator wires handlers to it
/// (<c>btn.Click += this.OnClick</c>), signature-checked at generate time.
/// </summary>
public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
