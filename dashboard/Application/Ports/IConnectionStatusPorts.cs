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

namespace Dashboard.Application.Ports;

public enum ConnectionStateKind
{
    Connecting,
    Connected,
    Disconnected,
    Retrying
}

public sealed record ConnectionStatus(
    ConnectionStateKind State,
    DateTimeOffset? NextRetryAt,
    int Attempt,
    string? LastError)
{
    public static ConnectionStatus Connecting()
    {
        return new ConnectionStatus(ConnectionStateKind.Connecting, null, 0, null);
    }

    public static ConnectionStatus Connected()
    {
        return new ConnectionStatus(ConnectionStateKind.Connected, null, 0, null);
    }

    public static ConnectionStatus Disconnected(string? error)
    {
        return new ConnectionStatus(ConnectionStateKind.Disconnected, null, 0, error);
    }

    public static ConnectionStatus Retrying(DateTimeOffset nextRetryAt, int attempt, string? error)
    {
        return new ConnectionStatus(ConnectionStateKind.Retrying, nextRetryAt, attempt, error);
    }
}

public interface IConnectionStatusReader
{
    ConnectionStatus Get();
    event Action? Changed;
}

public interface IConnectionStatusWriter
{
    void Update(ConnectionStatus status);
}
