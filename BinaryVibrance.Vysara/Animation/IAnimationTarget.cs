namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// Something an animation drives on an object: an animatable dependency property, or one of the element channels
/// that animate a property which is not a dependency property. Resolved once when an animation begins.
/// </summary>
internal interface IAnimationTarget
{
	/// <summary>The property's name, for diagnostics.</summary>
	string Name { get; }

	/// <summary>The name of the value type, for diagnostics.</summary>
	string ValueTypeName { get; }

	/// <summary>Bind an <see cref="ObjectAnimationUsingKeyFrames"/> to this target on <paramref name="owner"/>: a write of
	/// its key values, which it holds as objects, unboxed to the target's own type.</summary>
	ObjectBinding BindObjects(DependencyObject owner);
}

/// <summary>An <see cref="IAnimationTarget"/> of value type <typeparamref name="T"/>: read, write and release, unboxed.</summary>
internal interface IAnimationTarget<T> : IAnimationTarget
{
	/// <summary>The value the object shows now, including any animation holding it: where a new animation starts.</summary>
	T Read(DependencyObject target);

	/// <summary>The value the object shows once no animation drives it: what <see cref="Read"/> gives after
	/// <see cref="Release"/>.</summary>
	T ReadUnanimated(DependencyObject target);

	/// <summary>Write an animated value. It outranks the object's own value until <see cref="Release"/>.</summary>
	void Write(DependencyObject target, T value);

	/// <summary>Drop the animated value, so the object shows its own value again.</summary>
	void Release(DependencyObject target);

	/// <summary>Whether the target checks the values an animation names (<see cref="Validate"/>).</summary>
	bool Validates { get; }

	/// <summary>Check a value an animation names, as it begins, throwing on one the target refuses.</summary>
	void Validate(DependencyObject target, T value);

	// Every typed target binds an object animation the same way, through its own typed read, write and release.
	ObjectBinding IAnimationTarget.BindObjects(DependencyObject owner) => new ObjectBinding<T>(owner, this);
}
