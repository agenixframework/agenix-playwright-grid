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
///     Represents the roles that a user can have in a project.
/// </summary>
public enum ProjectRole
{
    /// <summary>
    ///     The user has the role of a project manager.
    /// </summary>
    [JsonPropertyName("PROJECT_MANAGER")] ProjectManager,

    /// <summary>
    ///     The user has the role of a member.
    /// </summary>
    [JsonPropertyName("MEMBER")] Member,

    /// <summary>
    ///     The user has the role of an operator.
    /// </summary>
    [JsonPropertyName("OPERATOR")] Operator,

    /// <summary>
    ///     The user has the role of a customer.
    /// </summary>
    [JsonPropertyName("CUSTOMER")] Customer
}
