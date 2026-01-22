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

namespace WorkerService.Infrastructure.Adapters;

public interface IPlaywrightProtocolClient : IDisposable
{
    Task ConnectAsync(string wsEndpoint, TimeSpan timeout, CancellationToken ct = default);
    Task<string?> SendCommandAsync(string method, TimeSpan timeout, CancellationToken ct = default);
    Task CloseAsync(CancellationToken ct = default);
}

public interface IPlaywrightProtocolClientFactory
{
    IPlaywrightProtocolClient CreateClient();
}

public sealed class PlaywrightProtocolClientFactory : IPlaywrightProtocolClientFactory
{
    public IPlaywrightProtocolClient CreateClient() => new PlaywrightProtocolClient();
}
