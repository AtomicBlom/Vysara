namespace SUIsei.Animation;

/// <summary>
/// An <see cref="ObjectAnimationUsingKeyFrames"/> bound to its target, made by the target
/// (<see cref="IAnimationTarget.BindObjects"/>) when the animation first begins. The key values, which the animation holds
/// as objects, are written to the target's typed property unboxed, and the value the target showed when a run began is
/// held typed, so neither a frame nor a begin allocates.
/// </summary>
internal abstract class ObjectBinding
{
	/// <summary>The property bound to, for diagnostics.</summary>
	public abstract IAnimationTarget Property { get; }

	/// <summary>Whether the property can hold <paramref name="value"/>: one of its type, or null where it takes null.</summary>
	public abstract bool Accepts(object? value);

	/// <summary>Check a value the property <see cref="Accepts"/> against its own rules, throwing on one it refuses.</summary>
	public abstract void Validate(object? value);

	/// <summary>Record the value the property shows now: what the animation holds before its first key.</summary>
	public abstract void CaptureStart();

	/// <summary>Write the value recorded by <see cref="CaptureStart"/>.</summary>
	public abstract void WriteStart();

	/// <summary>Write a key's value, which the property <see cref="Accepts"/>.</summary>
	public abstract void Write(object? value);

	/// <summary>Drop the animated value, so the property shows its own value again.</summary>
	public abstract void Release();
}

/// <inheritdoc cref="ObjectBinding"/>
internal sealed class ObjectBinding<T>(DependencyObject owner, IAnimationTarget<T> target) : ObjectBinding
{
	private T _start = default!;

	public override IAnimationTarget Property => target;

	public override bool Accepts(object? value) => value is T || (value is null && default(T) is null);

	public override void Validate(object? value)
	{
		if (target.Validates)
			target.Validate(owner, (T)value!);
	}

	public override void CaptureStart() => _start = target.Read(owner);

	public override void WriteStart() => target.Write(owner, _start);

	public override void Write(object? value) => target.Write(owner, (T)value!);

	public override void Release()
	{
		target.Release(owner);
		_start = default!; // hold no brush or other object past the run
	}
}
