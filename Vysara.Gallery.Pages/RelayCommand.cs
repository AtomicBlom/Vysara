using System.Windows.Input;

namespace Vysara.Gallery;

/// <summary>A minimal <see cref="ICommand"/> for the gallery's Command demo (a NativeAOT-clean stand-in for
/// CommunityToolkit.Mvvm's <c>[RelayCommand]</c>).</summary>
internal sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

	public void Execute(object? parameter) => execute(parameter);

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
