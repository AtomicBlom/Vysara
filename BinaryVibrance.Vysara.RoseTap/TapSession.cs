using System.Globalization;
using System.IO.Pipes;

using RoseMcp.Tap.Protocol;

namespace BinaryVibrance.Vysara.RoseTap;

/// <summary>
/// One connection to a RoseMCP host: connect to its pipe, greet it with the key and the capabilities this tap serves,
/// then answer each framed request until the host goes, asks the tap to detach, or the session is ended.
/// <para>
/// All of it happens on a reader thread of the session's own. A command that reads or changes the UI is handed to the
/// game's UI thread with <see cref="Diagnostics.Post(Action)"/>, which runs it at the next tick (so the tap assumes every
/// host ticks on one thread), and the reader waits for it
/// within a bound; a game that stops ticking leaves the host's request unanswered, which the host's own bound and its
/// warning that the request may still land already cover. <c>detach</c> is answered on the reader thread.
/// </para>
/// </summary>
internal sealed class TapSession
{
	// What this tap serves. A host refuses anything not named here without sending it, so a command is added here only
	// once it is served. SetResource is left out of apply: Vysara's StaticResource is resolved as the tree is built, so
	// replacing one changes nothing on screen.
	private static readonly TapCapabilities Capabilities = TapCapabilities.Of(
		TapCommands.Tree,
		TapCommands.Properties,
		TapCommands.PropertiesWritable,
		TapCommands.SelectHandle,
		TapCommands.Detach,
		$"{TapCommands.Apply}:{TapCommands.AddChild},{TapCommands.ClearProperty},{TapCommands.RemoveChild},{TapCommands.SetProperty}",
		$"{TapCommands.Select}:{TapCommands.SelectPick},{TapCommands.SelectRulers}",
		TapCommands.Idle,
		TapCommands.Deselect,
		TapCommands.Selection,
		TapCommands.Ask,
		TapCommands.Changes);

	private static readonly TimeSpan ConnectBound = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan UIThreadBound = TimeSpan.FromSeconds(30);

	private readonly string _pipeName;
	private readonly string _key;
	private readonly TapLog _log;
	private readonly TapHandles _handles = new();
	private readonly TapEdits _edits;
	private readonly TapOverlay _overlay;

	private readonly CancellationTokenSource _stopping = new();
	private readonly NamedPipeClientStream _pipe;
	private readonly Thread _reader;
	private int _released;

	public TapSession(string workDir, string pipeName, string key, TapLog log)
	{
		_pipeName = pipeName;
		_key = key;
		_log = log;
		_edits = new TapEdits(_handles);
		_overlay = new TapOverlay(_handles, log);
		_pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		_reader = new Thread(Run) { IsBackground = true, Name = "vysara-rosetap" };
		_log.Write($"Session for pipe {pipeName}, working in {workDir}.");
	}

	/// <summary>Start the reader thread, which connects and serves.</summary>
	public void Begin() => _reader.Start();

	/// <summary>
	/// End the session from outside it: stop the reader, close the pipe, and drop every handle and the pick. Waits a
	/// bounded time for the reader, which a pending read or a wait on the UI thread gives up at once on cancellation.
	/// </summary>
	public void End()
	{
		_stopping.Cancel();
		if (Thread.CurrentThread != _reader && _reader.IsAlive)
			_reader.Join(TimeSpan.FromSeconds(2));
		Release();
		_pipe.Dispose();
	}

	/// <summary>
	/// The answer to one request, and whether it ends the session. A null answer is not sent: the UI thread did not run
	/// the command within the bound, and the host's own bound reports that.
	/// </summary>
	public (string? Reply, bool Ends) Serve(string request)
	{
		var line = request.Split('\n', 2)[0];
		var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var command = words.Length > 0 ? words[0] : "";
		switch (command)
		{
			case TapCommands.Tree:
				return (OnUIThread(() => TapTree.Rows(_handles), onFailure: ""), false);

			case TapCommands.Properties:
				{
					var handle = HandleIn(words);
					var includeDefaults = words.Length > 2 && words[2] == "all";
					return (OnUIThread(
						() => _handles.Resolve(handle) is { } element ? TapProperties.Rows(element, includeDefaults) : TapRows.PropertiesError,
						onFailure: TapRows.PropertiesError), false);
				}

			case TapCommands.Apply:
				{
					var commands = request.Split('\n').Skip(1).Select(TapRows.ReadCommand).OfType<TapApplyCommand>().ToList();
					return (OnUIThread(() =>
					{
						var rows = _edits.Apply(commands);
						_overlay.Record(_edits.Changes);
						return rows;
					}, onFailure: ""), false);
				}

			case TapCommands.SelectHandle:
				{
					var handle = HandleIn(words);
					return (OnUIThread(() => _overlay.SelectByHandle(handle) ? "selected" : "none", onFailure: "none"), false);
				}

			case TapCommands.Select:
				{
					var rulers = words.Contains(TapCommands.SelectRulers);
					var all = words.Contains("all");
					bool? mine = words.Contains("myxaml") ? true : words.Contains("nomyxaml") ? false : null;
					var armed = OnUIThread(() =>
					{
						if (mine is { } justMine)
							_overlay.SetJustMyMarkup(justMine);
						_overlay.Arm(rulers, all);
						return "armed";
					}, onFailure: "");
					if (armed != "armed")
						return (armed, false);

					// The capture layer is laid out in the tick that armed it, so its extent is read at the next one.
					return (OnUIThread(() =>
					{
						var (width, height) = _overlay.ArmedExtent();
						return TapRows.Armed(width, height);
					}, onFailure: TapRows.Armed(0, 0)), false);
				}

			case TapCommands.Idle:
				return (OnUIThread(() =>
				{
					_overlay.GoIdle();
					return "idle";
				}, onFailure: ""), false);

			case TapCommands.Deselect:
				return (OnUIThread(() => _overlay.Deselect() ? "cleared" : "nothing", onFailure: ""), false);

			case TapCommands.Selection:
				return (OnUIThread(_overlay.Selection, onFailure: ""), false);

			case TapCommands.Ask:
				{
					// Answered here on the reader thread: the latch holds no UI, so asking costs the game nothing. Only the read
					// that tells the toolbar its press arrived touches the UI, to stop the button showing a press waiting.
					var seen = words.Length > 1 && long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var held) ? held : 0;
					var reply = TapAsk.Reply(seen, out var arrived);
					if (arrived)
						PostToUIThread(_overlay.Chrome);
					return (reply, false);
				}

			case TapCommands.Changes:
				{
					// Anything but "on" turns them off, the direction that leaves less drawn over somebody's game.
					var wanted = words.Length > 1 && words[1] == "on";
					return (OnUIThread(() =>
					{
						_overlay.ShowChanges(wanted);
						return _overlay.ChangesShown ? "on" : "off";
					}, onFailure: ""), false);
				}

			case TapCommands.Detach:
				return (Release() ? "released" : "already released", true);

			default:
				// An empty reply is how a tap says it does not serve a request, which a host tells from a lost message.
				_log.Write($"No handler for '{line}'; answering empty.");
				return ("", false);
		}
	}

	private void Run() => RunAsync(_stopping.Token).GetAwaiter().GetResult();

	private async Task RunAsync(CancellationToken stopping)
	{
		try
		{
			await _pipe.ConnectAsync(ConnectBound, stopping).ConfigureAwait(false);
			await TapWire.WriteMessageAsync(_pipe, TapGreeting.Write(_key, Capabilities), stopping).ConfigureAwait(false);
			_log.Write($"Connected to {_pipeName} and greeted, serving: {Capabilities}");

			// The toolbar goes up as the session starts, so a person can pick without the agent asking first.
			PostToUIThread(_overlay.Install);

			while (!stopping.IsCancellationRequested)
			{
				var message = await TapWire.ReadMessageAsync(_pipe, stopping).ConfigureAwait(false);
				if (message is null)
				{
					_log.Write("The host has gone.");
					break;
				}
				if (!TapWire.TryReadFrame(message, out var id, out var body))
				{
					_log.Write("Dropped a message that carries no request id.");
					continue;
				}

				var (reply, ends) = Serve(body);
				if (reply is not null)
					await TapWire.WriteMessageAsync(_pipe, TapWire.Frame(id, reply), stopping).ConfigureAwait(false);
				if (ends)
				{
					_log.Write("Detached at the host's request.");
					break;
				}
			}
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException)
		{
			_log.Write($"The session on {_pipeName} ended: {exception.GetType().Name}: {exception.Message}");
		}
		catch (Exception exception)
		{
			_log.Write($"The session on {_pipeName} failed: {exception}");
		}
		finally
		{
			Release();
			_pipe.Dispose();
		}
	}

	// Run work on the UI thread at the next tick and wait for its answer. A failure there is logged and answered with
	// onFailure, so one bad element cannot take the session down; a tick that does not come within the bound answers
	// null.
	private string? OnUIThread(Func<string> work, string onFailure)
	{
		var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		try
		{
			Diagnostics.Post(() =>
			{
				try
				{
					answer.TrySetResult(work());
				}
				catch (Exception exception)
				{
					_log.Write($"A command failed on the UI thread: {exception}");
					answer.TrySetResult(onFailure);
				}
			});
		}
		catch (InvalidOperationException exception)
		{
			_log.Write($"Could not reach the UI thread: {exception.Message}");
			return onFailure;
		}

		try
		{
			if (answer.Task.Wait(UIThreadBound, _stopping.Token))
				return answer.Task.Result;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		_log.Write($"The UI thread did not run the command within {UIThreadBound.TotalSeconds}s; it may still run.");
		return null;
	}

	// Drops everything the session holds in the game, once: the overlay and its hold on the pointer, the handles, and a
	// press of Ask nobody collected. False when it already had.
	private bool Release()
	{
		if (Interlocked.Exchange(ref _released, 1) != 0)
			return false;
		PostToUIThread(_overlay.Remove);
		_handles.Clear();
		TapAsk.Forget();
		return true;
	}

	// Run work at the next tick without waiting for it.
	private void PostToUIThread(Action work)
	{
		try
		{
			Diagnostics.Post(work);
		}
		catch (InvalidOperationException exception)
		{
			_log.Write($"Could not reach the UI thread: {exception.Message}");
		}
	}

	private static ulong HandleIn(string[] words) =>
		words.Length > 1 && ulong.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var handle) ? handle : 0;
}
