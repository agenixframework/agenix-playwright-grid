using System;
using Dashboard.Application.Ports;

namespace Dashboard;

/// <summary>
///     Thread-safe proxy that stores the latest pool state for the dashboard.
///     Implements both read and write ports to act as the application state holder.
/// </summary>
internal sealed class PoolStateProxy : IPoolStateReader, IPoolStateWriter
{
    private readonly object _gate = new();
    private PoolStateDto _state;

    public event Action Changed;

    public PoolStateDto Get()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    public void Update(PoolStateDto state)
    {
        lock (_gate)
        {
            _state = state;
        }

        var handler = Changed;
        handler?.Invoke();
    }
}
