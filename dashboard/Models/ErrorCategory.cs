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

namespace Dashboard.Models;

/// <summary>
/// Categorizes HTTP errors to determine appropriate handling and retry strategies.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// Network-level connectivity issues.
    /// </summary>
    Network,

    /// <summary>
    /// Server-side errors (5xx status codes).
    /// </summary>
    Server,

    /// <summary>
    /// Client-side request errors (4xx status codes).
    /// </summary>
    Client,

    /// <summary>
    /// Rate limit exhaustion (429 status code).
    /// </summary>
    RateLimit,

    /// <summary>
    /// Data validation failures.
    /// </summary>
    Validation,

    /// <summary>
    /// Unclassified or unexpected errors.
    /// </summary>
    Unknown
}
