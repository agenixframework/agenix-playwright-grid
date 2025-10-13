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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses;

/// <summary>
///     Represents a container for test item history.
/// </summary>
public class TestItemHistoryContainer
{
    /// <summary>
    ///     Gets or sets the grouping field.
    /// </summary>
    public string GroupingField { get; set; }

    /// <summary>
    ///     Gets or sets the list of test item responses.
    /// </summary>
    public IList<TestItemResponse> Resources { get; set; }
}
