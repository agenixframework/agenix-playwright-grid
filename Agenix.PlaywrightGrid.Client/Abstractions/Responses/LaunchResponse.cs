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

public class LaunchResponse
{
    public long Id { get; set; }
    public string Uuid { get; set; }
    public long DbId { get; set; } // Globally unique sequential ID per project
    public string Name { get; set; }
    public string Description { get; set; }
    public int Number { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverterEx<LaunchMode>))]
    public LaunchMode Mode { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool HasRetries { get; set; }
    public IList<ItemAttribute> Attributes { get; set; }
    public Statistic Statistics { get; set; }
}

public class Statistic
{
    public Executions Executions { get; set; }
    public Defects Defects { get; set; }
}

public class Executions
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}

public class Defects
{
    [JsonPropertyName("product_bug")] public Defect ProductBugs { get; set; }

    [JsonPropertyName("automation_bug")] public Defect AutomationBugs { get; set; }

    [JsonPropertyName("system_issue")] public Defect SystemIssues { get; set; }

    [JsonPropertyName("to_investigate")] public Defect ToInvestigate { get; set; }

    [JsonPropertyName("no_defect")] public Defect NoDefect { get; set; }
}

public class Defect
{
    /// <summary>
    ///     Gets or sets the total number of defects.
    /// </summary>
    public int Total { get; set; }
}
