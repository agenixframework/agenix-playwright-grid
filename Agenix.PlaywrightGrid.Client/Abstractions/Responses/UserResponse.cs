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
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses;

/// <summary>
///     Represents a response containing user information.
/// </summary>
public class UserResponse
{
    /// <summary>
    ///     Gets or sets the full name of the user.
    /// </summary>
    public string Fullname { get; set; }

    /// <summary>
    ///     Gets or sets the email of the user.
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    ///     Gets or sets the assigned projects of the user.
    /// </summary>
    public IDictionary<string, ProjectAssigment> AssignedProjects { get; set; }
}

/// <summary>
///     Represents a project assignment for a user.
/// </summary>
public class ProjectAssigment
{
    /// <summary>
    ///     Gets or sets the role of the project assignment.
    /// </summary>
    [JsonPropertyName("projectRole")]
    [JsonConverter(typeof(JsonStringEnumConverterEx<ProjectRole>))]
    public ProjectRole ProjectRole { get; set; }
}
