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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Models;

/// <summary>
///     Represents a struct that defines well-known issue types.
/// </summary>
public struct WellKnownIssueType
{
    /// <summary>
    ///     Represents a product bug issue type.
    /// </summary>
    public const string ProductBug = "PB001";

    /// <summary>
    ///     Represents an automation bug issue type.
    /// </summary>
    public const string AutomationBug = "AB001";

    /// <summary>
    ///     Represents a system issue type.
    /// </summary>
    public const string SystemIssue = "SI001";

    /// <summary>
    ///     Represents an issue type that needs to be investigated.
    /// </summary>
    public const string ToInvestigate = "TI001";

    /// <summary>
    ///     Represents an issue type that is not a defect.
    /// </summary>
    public const string NotDefect = "ND001";
}
