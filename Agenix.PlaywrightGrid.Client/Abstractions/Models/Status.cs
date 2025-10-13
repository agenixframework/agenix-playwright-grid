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

using System.Text.Json.Serialization;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Models;

/// <summary>
///     Describes statuses of tests items.
///     Uses PascalCase serialization to match database constraints.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Status>))]
public enum Status
{
    /// <summary>
    ///     Test item is in progress.
    /// </summary>
    InProgress,

    /// <summary>
    ///     Test item has passed.
    /// </summary>
    Passed,

    /// <summary>
    ///     Test item has failed.
    /// </summary>
    Failed,

    /// <summary>
    ///     Test item has been skipped.
    /// </summary>
    Skipped,

    /// <summary>
    ///     Test item has been interrupted.
    /// </summary>
    Interrupted,

    /// <summary>
    ///     Test item has been cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    ///     Test item has been stopped.
    /// </summary>
    Stopped,

    /// <summary>
    ///     Test item provides information.
    /// </summary>
    Info,

    /// <summary>
    ///     Test item has a warning.
    /// </summary>
    Errored
}
