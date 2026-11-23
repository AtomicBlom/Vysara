using System.Windows.Input;

using Vysara.Controls;

namespace Vysara.Tests;

/// <summary>
/// <see cref="ButtonBase.Command"/> / <see cref="ButtonBase.CommandParameter"/>: a click
/// executes the command with the parameter, and while a command is set the button's <c>IsEnabled</c> tracks
/// <see cref="ICommand.CanExecute"/>.
/// </summary>
public sealed class CommandTests
{
	private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
	{
		public event EventHandler? CanExecuteChanged;

		public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

		public void Execute(object? parameter) => execute(parameter);

		public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}

	[Test]
	public void Click_executes_the_command_with_the_parameter()
	{
		object? received = null;
		var count = 0;
		var button = new Button { CommandParameter = "p" };
		button.Command = new RelayCommand(p => { received = p; count++; });

		button.PerformClick();

		count.ShouldBe(1);
		received.ShouldBe("p");
	}

	[Test]
	public void CanExecute_drives_IsEnabled()
	{
		var can = false;
		var command = new RelayCommand(_ => { }, _ => can);
		var button = new Button { Command = command };

		button.IsEnabled.ShouldBeFalse();   // CanExecute was false when the command was assigned

		can = true;
		command.RaiseCanExecuteChanged();

		button.IsEnabled.ShouldBeTrue();
	}

	/// <summary>A command's CanExecute is folded into the effective IsEnabled, as WinUI folds it, rather than written over the
	/// author's value: a button its author disabled stays disabled however the command changes, and keeps its own value.</summary>
	[Test]
	public void CanExecute_never_overrides_the_authors_IsEnabled()
	{
		var can = true;
		var command = new RelayCommand(_ => { }, _ => can);
		var button = new Button { IsEnabled = false, Command = command };

		button.IsEnabled.ShouldBeFalse(); // the author's false, though the command can execute
		can = false;
		command.RaiseCanExecuteChanged();
		can = true;
		command.RaiseCanExecuteChanged();
		button.IsEnabled.ShouldBeFalse();

		button.ClearValue(Control.IsEnabledProperty);
		button.IsEnabled.ShouldBeTrue(); // the command can, and the author no longer says otherwise
		can = false;
		command.RaiseCanExecuteChanged();
		button.IsEnabled.ShouldBeFalse();
		button.HasLocalValue(Control.IsEnabledProperty).ShouldBeFalse(); // the command never wrote one
	}

	[Test]
	public void Execute_is_gated_by_CanExecute()
	{
		var count = 0;
		var button = new Button { Command = new RelayCommand(_ => count++, _ => false) };

		button.PerformClick();

		count.ShouldBe(0);
	}
}
