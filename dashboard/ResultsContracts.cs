#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

namespace Dashboard;

public sealed record ResultRunSummaryDto
{
    public string RunId { get; init; } = string.Empty;
    public string? ExternalId { get; init; }
    public string App { get; init; } = string.Empty;
    public string Browser { get; init; } = string.Empty;
    public string Env { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? OS { get; init; }
    public string Status { get; set; } = "Queued";
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? WorkerNodeId { get; set; }
    public string? PlaywrightVersion { get; set; }
    public string? BrowserVersion { get; set; }
}

public sealed record CommandLogEventDto
{
    public string RunId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string Kind { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string>? Props { get; init; }
    public string? TestId { get; init; }
}

public sealed record ResultTestCaseDto
{
    public string RunId { get; init; } = string.Empty;
    public string TestId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string? Project { get; init; }
    public string Status { get; set; } = "Queued";
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStack { get; set; }
}
