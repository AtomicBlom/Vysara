using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Vysara.Tests.Sample;

/// <summary>Code-behind for <c>LiveBindingView.xaml</c>: the bind root.</summary>
internal sealed partial class LiveBindingView
{
	public LiveViewModel ViewModel { get; } = new();

	public LiveBindingView() => InitializeComponent();
}

/// <summary>The functions <c>LiveBindingView.xaml</c> binds through.</summary>
internal static class LiveFormat
{
	public static string Percent(float level) => (level * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

	public static string Pair(string name, float level) => $"{name} {Percent(level)}";
}

/// <summary>What <c>LiveBindingView.xaml</c> shows: a level it clamps to 0.8, a loop, and choices of which exactly one is
/// selected.</summary>
internal sealed class LiveViewModel : INotifyPropertyChanged
{
	public LiveViewModel()
	{
		foreach (var name in new[] { "a", "b", "c" })
			Choices.Add(new LiveChoice(this, name));
		Choices[0].Select();
	}

	public bool HasError { get; set => Set(ref field, value); }

	public float Level { get; set => Set(ref field, Math.Min(value, 0.8f)); } = 0.5f;

	public bool Loop { get; set => Set(ref field, value); }

	public ObservableCollection<LiveChoice> Choices { get; } = [];

	public LiveChoice? Selected { get; private set => Set(ref field, value); }

	internal void Choose(LiveChoice choice)
	{
		foreach (var other in Choices)
			other.Show(ReferenceEquals(other, choice));
		Selected = choice;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
			return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}

/// <summary>A choice: selecting it unselects the others; it can't be unselected by itself.</summary>
internal sealed class LiveChoice(LiveViewModel owner, string name) : INotifyPropertyChanged
{
	private bool _selected;

	public string Name { get; } = name;

	public string[] Tags { get; } = [name + "1", name + "2"];

	/// <summary>Setting true selects it; setting false is refused, as a radio button's is.</summary>
	public bool IsSelected
	{
		get => _selected;
		set
		{
			if (value && !_selected)
				owner.Choose(this);
		}
	}

	public float Level
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
		}
	} = 0.25f;

	public void Select() => IsSelected = true;

	internal void Show(bool selected)
	{
		if (_selected == selected)
			return;
		_selected = selected;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
