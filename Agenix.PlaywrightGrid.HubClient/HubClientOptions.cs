#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
///     Options for configuring HubClient when registering via DI.
/// </summary>
public sealed class HubClientOptions
{
    /// <summary>
    ///     The base URL of the Hub (e.g., http://127.0.0.1:5100).
    /// </summary>
    public string? HubUrl { get; set; }

    /// <summary>
    ///     The x-hub-secret header value used to authenticate runner requests.
    ///     If not set, HUB_RUNNER_SECRET environment variable or "runner-secret" is used.
    /// </summary>
    public string? RunnerSecret { get; set; }

    /// <summary>
    ///     The request timeout to use on HttpClient.
    ///     Defaults to 15 seconds if not specified.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
