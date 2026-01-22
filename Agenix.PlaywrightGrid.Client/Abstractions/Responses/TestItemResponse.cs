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

public class TestItemResponse
{
    public Guid Id { get; set; }
    public string Uuid { get; set; }
    public long? DbId { get; set; }

    [JsonPropertyName("parent")] public Guid? ParentId { get; set; }

    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public IList<TestItemResponse> Retries { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverterEx<Status>))]
    public Status Status { get; set; }

    [JsonPropertyName("itemType")]
    [JsonConverter(typeof(JsonStringEnumConverterEx<TestItemType>))]
    public TestItemType Type { get; set; }

    public Issue Issue { get; set; }
    public PathNames PathNames { get; set; }
    public bool HasChildren { get; set; }
    public IList<KeyValuePair<string, string>> Parameters { get; set; }
    public string UniqueId { get; set; }

    [JsonPropertyName("codeRef")] public string CodeReference { get; set; }

    [JsonConverter(typeof(AttributesConverter))]
    public IList<ItemAttribute> Attributes { get; set; }
}

public class PathNames
{
    public LaunchPathNameModel LaunchPathName { get; set; }
    public IList<ItemPathNameModel> ItemPaths { get; set; }

    public class LaunchPathNameModel
    {
        public string Name { get; set; }
        public long Number { get; set; }
    }

    public class ItemPathNameModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}

public class Issue
{
    [JsonPropertyName("issueType")] public string Type { get; set; }

    public string Comment { get; set; }

    public bool AutoAnalyzed { get; set; }

    public bool IgnoreAnalyzer { get; set; }

    public IList<ExternalSystemIssue> ExternalSystemIssues { get; set; }
}

public class ExternalSystemIssue
{
    public DateTime SubmitDate { get; set; }

    public string Submitter { get; set; }

    public string SystemId { get; set; }

    public string TicketId { get; set; }

    public string Url { get; set; }

    public string BtsProject { get; set; }

    public string BtsUrl { get; set; }
}
