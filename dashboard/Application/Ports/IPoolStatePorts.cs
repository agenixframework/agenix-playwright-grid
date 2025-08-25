using System;
using Dashboard;

namespace Dashboard.Application.Ports;

/// <summary>
/// Read-only port for accessing current pool state and subscribing to changes.
/// </summary>
public interface IPoolStateReader
{
    PoolStateDto Get();
    event Action Changed;
}

/// <summary>
/// Write-only port for updating the pool state from infrastructure adapters.
/// </summary>
public interface IPoolStateWriter
{
    void Update(PoolStateDto state);
}
