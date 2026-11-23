namespace BinaryVibrance.Vysara.RoutedEvents;

/// <summary>
/// The single routed-event delegate shape — typed, so invocation is a direct call with no
/// <c>MethodInfo.Invoke</c> and no argument boxing (the NAOT requirement). The generator wires handlers to it
/// (<c>btn.Click += this.OnClick</c>), signature-checked at generate time.
/// </summary>
public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
