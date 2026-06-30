using MindedOS.Core;

namespace MindedOS.Sensor;

public enum LinkState { Idle, Searching, Streaming, NotReachable, Dropped, Unsupported }

/// <summary>
/// A single shared source of EEG events. The shell owns one instance and every
/// sub-program subscribes to it — they never open the port themselves.
/// </summary>
public interface IEegSource
{
    LinkState State { get; }
    string SourceName { get; }

    event Action<EegEvent>? Event;
    event Action<LinkState>? StateChanged;

    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}
