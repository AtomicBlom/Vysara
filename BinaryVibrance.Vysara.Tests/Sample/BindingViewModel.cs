using System.ComponentModel;

namespace BinaryVibrance.Vysara.Tests.Sample;

/// <summary>A tiny INPC view model for the x:Bind generator proof (<c>BindingView.xaml</c>).</summary>
internal sealed class BindingViewModel : INotifyPropertyChanged
{
	public string Caption
	{
		get;
		set { if (field != value) { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption))); } }
	} = "start";

	public event PropertyChangedEventHandler? PropertyChanged;
}
