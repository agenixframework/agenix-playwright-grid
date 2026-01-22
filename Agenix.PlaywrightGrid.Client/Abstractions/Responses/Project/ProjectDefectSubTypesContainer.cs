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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses.Project;

/// <summary>
///     Represents a container for project defect subtypes.
/// </summary>
public class ProjectDefectSubTypesContainer
{
    [JsonPropertyName("PRODUCT_BUG")] public IList<ProjectDefectSubType> ProductBugTypes { get; set; }

    [JsonPropertyName("AUTOMATION_BUG")] public IList<ProjectDefectSubType> AutomationBugTypes { get; set; }

    [JsonPropertyName("SYSTEM_ISSUE")] public IList<ProjectDefectSubType> SystemIssueTypes { get; set; }

    [JsonPropertyName("TO_INVESTIGATE")] public IList<ProjectDefectSubType> ToInvestigateTypes { get; set; }

    [JsonPropertyName("NO_DEFECT")] public IList<ProjectDefectSubType> NoDefectTypes { get; set; }
}
