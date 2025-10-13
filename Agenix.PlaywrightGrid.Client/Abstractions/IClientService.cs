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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;

namespace Agenix.PlaywrightGrid.Client.Abstractions;

/// <summary>
///     Main service interface for interacting with PlaywrightGrid hub.
/// </summary>
public interface IClientService : IDisposable
{
    /// <summary>
    ///     Base URI of the PlaywrightGrid hub.
    /// </summary>
    Uri BaseUri { get; }

    /// <summary>
    ///     Project key for organizing test results.
    /// </summary>
    string ProjectKey { get; }

    /// <summary>
    ///     Resource for launch operations.
    /// </summary>
    ILaunchResource Launch { get; }

    /// <summary>
    ///     Resource for test item operations (ReportPortal-aligned).
    ///     This is the recommended API for reporting test results.
    /// </summary>
    ITestItemResource TestItem { get; }

    /// <summary>
    ///     Resource for log item operations.
    /// </summary>
    ILogItemResource LogItem { get; }
}
