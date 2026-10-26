namespace SUIsei.Animation;

/// <summary>
/// Interpolates from <paramref name="from"/> to <paramref name="to"/>: <paramref name="t"/> 0 gives
/// <paramref name="from"/>, 1 gives <paramref name="to"/>, and an easing may push it outside that range. A dependency
/// property with one is animatable (<see cref="PropertyMetadata{TOwner, TValue}.Lerp"/>).
/// </summary>
public delegate T Lerp<T>(T from, T to, float t);
