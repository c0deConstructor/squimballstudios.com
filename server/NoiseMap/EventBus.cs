using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NoiseMap;

/// <summary>
/// Thread-safe fan-out event bus.  HoneypotService publishes; each connected
/// WebSocket client subscribes and gets its own buffered Channel.
/// </summary>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<NoiseEvent>> _subs = new();

    /// <summary>Subscribe and receive a reader for this client's private channel.</summary>
    public Guid Subscribe(out ChannelReader<NoiseEvent> reader)
    {
        var id = Guid.NewGuid();
        // Bounded so a slow/stuck client never fills memory.
        var ch = Channel.CreateBounded<NoiseEvent>(new BoundedChannelOptions(2000)
        {
            FullMode             = BoundedChannelFullMode.DropOldest,
            SingleWriter         = false,
            SingleReader         = true,
            AllowSynchronousContinuations = false,
        });
        _subs[id] = ch;
        reader = ch.Reader;
        return id;
    }

    /// <summary>Unsubscribe and close the client's channel.</summary>
    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch))
            ch.Writer.TryComplete();
    }

    /// <summary>Publish an event to every currently-connected client.</summary>
    public void Publish(NoiseEvent evt)
    {
        foreach (var (_, ch) in _subs)
            ch.Writer.TryWrite(evt);
    }

    public int SubscriberCount => _subs.Count;
}
