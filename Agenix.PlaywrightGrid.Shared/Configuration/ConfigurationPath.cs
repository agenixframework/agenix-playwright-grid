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

namespace Agenix.PlaywrightGrid.Shared.Configuration;

/// <summary>
///     Stores well known configuration property names.
/// </summary>
public static class ConfigurationPath
{
    /// <summary>
    ///     The delimiter used to separate keys in the configuration path.
    /// </summary>
    public static readonly string KeyDelimeter = ":";

    /// <summary>
    ///     The prefix used to identify appenders in the configuration path.
    /// </summary>
    public static readonly string AppenderPrefix = "++";

    /// <summary>
    ///     The configuration path for the server URL.
    /// </summary>
    public static readonly string ServerUrl = $"Server{KeyDelimeter}Url";

    /// <summary>
    ///     The configuration path for the server project.
    /// </summary>
    public static readonly string ServerProject = $"Server{KeyDelimeter}Project";

    /// <summary>
    ///     The configuration path for the server authentication UUID.
    /// </summary>
    [Obsolete("'Server:Authentication:Uuid' parameter is deprecated. Use 'Server:ApiKey' instead.")]
    public static readonly string ServerAuthenticationUuid = $"Server{KeyDelimeter}Authentication{KeyDelimeter}Uuid";

    /// <summary>
    ///     The configuration path for the server authentication key.
    /// </summary>
    public static readonly string ServerAuthenticationKey = $"Server{KeyDelimeter}ApiKey";

    /// <summary>
    ///     The configuration path for the logs batch capacity.
    /// </summary>
    public static readonly string LogsBatchCapacity = $"Server{KeyDelimeter}LogsBatchCapacity";

    /// <summary>
    ///     The configuration path for async reporting.
    /// </summary>
    public static readonly string AsyncReporting = $"Server{KeyDelimeter}AsyncReporting";

    /// <summary>
    ///     The configuration path for the launch name.
    /// </summary>
    public static readonly string LaunchName = $"Launch{KeyDelimeter}Name";

    /// <summary>
    ///     The configuration path for the launch description.
    /// </summary>
    public static readonly string LaunchDescription = $"Launch{KeyDelimeter}Description";

    /// <summary>
    ///     The configuration path for the launch debug mode.
    /// </summary>
    public static readonly string LaunchDebugMode = $"Launch{KeyDelimeter}DebugMode";

    /// <summary>
    ///     The configuration path for the launch tags.
    /// </summary>
    public static readonly string LaunchTags = $"Launch{KeyDelimeter}Tags";
}
