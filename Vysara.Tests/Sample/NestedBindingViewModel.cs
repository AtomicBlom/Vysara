using System.ComponentModel;

namespace Vysara.Tests.Sample;

/// <summary>
/// A multi-level INPC model graph for the deep-<c>{x:Bind}</c> generator proof. Each level raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>, and the <see cref="DemoModel.Bloom"/> level is nullable —
/// so <c>{x:Bind ViewModel.Demo.Bloom.Intensity}</c> exercises leaf-change propagation, intermediate-swap
/// re-subscription, and the per-level null safety of the generated read/subscribe chain.
/// </summary>
internal sealed class NestedBindingViewModel : INotifyPropertyChanged
{
	public DemoModel Demo
	{
		get;
		set { if (!ReferenceEquals(field, value)) { field = value; Raise(nameof(Demo)); } }
	} = new();

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	internal sealed class DemoModel : INotifyPropertyChanged
	{
		public string Title
		{
			get;
			set { if (field != value) { field = value; Raise(nameof(Title)); } }
		} = "Demo A";

		public BloomModel? Bloom
		{
			get;
			set { if (!ReferenceEquals(field, value)) { field = value; Raise(nameof(Bloom)); } }
		} = new();

		public event PropertyChangedEventHandler? PropertyChanged;

		private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	internal sealed class BloomModel : INotifyPropertyChanged
	{
		public string Intensity
		{
			get;
			set { if (field != value) { field = value; Raise(nameof(Intensity)); } }
		} = "0.50";

		public event PropertyChangedEventHandler? PropertyChanged;

		private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
