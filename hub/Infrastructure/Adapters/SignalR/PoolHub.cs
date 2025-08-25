using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.SignalR;

// Dashboard client contract
/// <summary>
/// Defines a contract for SignalR clients in the dashboard context to receive updates.
/// This interface includes methods for delivering updates about the pool's state
/// and worker status to connected clients in real-time.
/// </summary>
/// <remarks>
/// Used by the <see cref="PoolHub"/> to push notifications and data updates to
/// dashboard clients. Implementations are generated and invoked by the SignalR
/// framework during client-server communication.
/// </remarks>
public interface IPoolClient
{
    /// <summary>
    /// Sends the current state of the pool to connected SignalR clients.
    /// </summary>
    /// <param name="state">The state of the pool, including pool entries, worker statuses, and timestamp, encapsulated in a data transfer object.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PoolState(PoolStateDto state);

    /// <summary>
    /// Sends the current status of a specific worker to connected SignalR clients.
    /// </summary>
    /// <param name="worker">The status of the worker, including its identifiers, labels, active pools, and other relevant metadata encapsulated in a data transfer object.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task WorkerUpdate(WorkerStatusDto worker);
}

// SignalR hub that serves dashboard clients
/// <summary>
/// Represents a SignalR hub that serves as a communication interface for dashboard clients.
/// This hub facilitates the interaction between connected clients and the backend,
/// allowing the current pool state to be transmitted to connected clients in real-time.
/// </summary>
/// <remarks>
/// This class extends the <see cref="Hub{T}"/> class, where T is the client interface.
/// The hub uses dependency injection to access the <see cref="IPoolStateReader"/>,
/// which provides the current state of the pool.
/// </remarks>
public sealed class PoolHub(IPoolStateReader reader) : Hub<IPoolClient>
{
    /// <summary>
    /// Handles a new client connection to the SignalR hub.
    /// Invoked when a connection with the hub is established.
    /// Sends the current pool state to the newly connected client.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.PoolState(await reader.GetStateAsync());
        await base.OnConnectedAsync();
    }
}
