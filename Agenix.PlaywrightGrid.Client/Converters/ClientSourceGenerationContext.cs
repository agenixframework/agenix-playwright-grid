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
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses.Project;

namespace Agenix.PlaywrightGrid.Client.Converters;

/// <inheritdoc />
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(StartLaunchRequest))]
[JsonSerializable(typeof(FinishLaunchRequest))]
[JsonSerializable(typeof(UpdateLaunchRequest))]
[JsonSerializable(typeof(AnalyzeLaunchRequest))]
[JsonSerializable(typeof(MergeLaunchesRequest))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(LaunchResponse))]
[JsonSerializable(typeof(Content<LaunchResponse>))]
[JsonSerializable(typeof(LaunchCreatedResponse))]
[JsonSerializable(typeof(LaunchFinishedResponse))]
[JsonSerializable(typeof(StartTestItemRequest))]
[JsonSerializable(typeof(FinishTestItemRequest))]
[JsonSerializable(typeof(UpdateTestItemRequest))]
[JsonSerializable(typeof(AssignTestItemIssuesRequest))]
[JsonSerializable(typeof(TestItemResponse))]
[JsonSerializable(typeof(Content<TestItemResponse>))]
[JsonSerializable(typeof(TestItemCreatedResponse))]
[JsonSerializable(typeof(IList<Issue>))]
[JsonSerializable(typeof(Content<TestItemHistoryContainer>))]
[JsonSerializable(typeof(CreateLogItemRequest))]
[JsonSerializable(typeof(CreateLogItemRequest[]))]
[JsonSerializable(typeof(LogItemResponse))]
[JsonSerializable(typeof(Content<LogItemResponse>))]
[JsonSerializable(typeof(LogItemCreatedResponse))]
[JsonSerializable(typeof(LogItemsCreatedResponse))]
[JsonSerializable(typeof(CreateUserFilterRequest))]
[JsonSerializable(typeof(UpdateUserFilterRequest))]
[JsonSerializable(typeof(UserFilterResponse))]
[JsonSerializable(typeof(Content<UserFilterResponse>))]
[JsonSerializable(typeof(UserFilterCreatedResponse))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(ProjectResponse))]
[JsonSerializable(typeof(PreferenceResponse))]
internal partial class ClientSourceGenerationContext : JsonSerializerContext
{
}
