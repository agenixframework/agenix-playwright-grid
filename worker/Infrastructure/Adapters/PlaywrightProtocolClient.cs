#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WorkerService.Infrastructure.Adapters;

/// <summary>
///     Lightweight client for sending Playwright protocol commands over WebSocket.
///     Used for browser health checks without spawning external processes.
/// </summary>
public sealed class PlaywrightProtocolClient : IPlaywrightProtocolClient
{
    private readonly ClientWebSocket _ws = new();
    private bool _disposed;

    /// <summary>
    ///     Connects to a Playwright browser server's WebSocket endpoint.
    /// </summary>
    /// <param name="wsEndpoint">WebSocket URL (e.g., ws://localhost:PORT/ws)</param>
    /// <param name="timeout">Connection timeout</param>
    /// <param name="ct">Cancellation token</param>
    public async Task ConnectAsync(string wsEndpoint, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PlaywrightProtocolClient));

        var uri = new Uri(wsEndpoint);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        await _ws.ConnectAsync(uri, cts.Token);
    }

    /// <summary>
    ///     Sends a Playwright protocol command and waits for a response.
    /// </summary>
    /// <param name="method">Protocol method (e.g., "Browser.version")</param>
    /// <param name="timeout">Response timeout</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Response JSON as string, or null if timeout/error</returns>
    public async Task<string?> SendCommandAsync(string method, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PlaywrightProtocolClient));

        if (_ws.State != WebSocketState.Open)
            return null;

        // Construct protocol message: {"id":1,"method":"Browser.version","params":{}}
        var message = new
        {
            id = 1,
            method,
            @params = new { }
        };
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            // Send command
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cts.Token);

            // Receive response
            var buffer = new byte[4096];
            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            return response;
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation
            return null;
        }
        catch (WebSocketException)
        {
            // Connection error
            return null;
        }
    }

    /// <summary>
    ///     Closes the WebSocket connection gracefully.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_disposed || _ws.State == WebSocketState.Closed || _ws.State == WebSocketState.Aborted)
            return;

        try
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check complete", ct);
        }
        catch
        {
            // Ignore errors during close
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ws.Dispose();
    }
}
