using System.Collections;
using System.Collections.Specialized;

namespace Vysara.Controls.Primitives;

/// <summary>
/// A control's hold on its items source's <see cref="INotifyCollectionChanged.CollectionChanged"/>, taken only while the
/// control is in a live tree, so a long-lived collection (a view model's) never keeps a removed control alive. Joining a
/// tree again re-reads the source, since changes made while it was out were not heard.
/// </summary>
internal sealed class SourceSubscription(NotifyCollectionChangedEventHandler changed, Action resync)
{
	private IEnumerable? _source;
	private INotifyCollectionChanged? _listening;
	private bool _live;

	/// <summary>The source now set; listened to while live.</summary>
	public IEnumerable? Source
	{
		get => _source;
		set
		{
			_source = value;
			Listen();
		}
	}

	/// <summary>The control joined (true) or left (false) a live tree.</summary>
	public void LiveTreeChanged(bool attached)
	{
		_live = attached;
		Listen();
		if (attached)
			resync();
	}

	private void Listen()
	{
		var wanted = _live ? _source as INotifyCollectionChanged : null;
		if (ReferenceEquals(wanted, _listening))
			return;
		if (_listening is not null)
			_listening.CollectionChanged -= changed;
		_listening = wanted;
		if (wanted is not null)
			wanted.CollectionChanged += changed;
	}
}
