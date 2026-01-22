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

using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Defines a request for assigning issues to test items.
/// </summary>
public class AssignTestItemIssuesRequest
{
    /// <summary>
    ///     Gets or sets the list of test items and their issues.
    /// </summary>
    public List<TestItemIssueUpdate> Issues { get; set; }
}

public class TestItemIssueUpdate
{
    /// <summary>
    ///     Gets or sets the issue of the test item.
    /// </summary>
    public Issue Issue { get; set; }

    /// <summary>
    ///     Gets or sets the ID of the test item to assign the issue.
    /// </summary>
    public long TestItemId { get; set; }
}
