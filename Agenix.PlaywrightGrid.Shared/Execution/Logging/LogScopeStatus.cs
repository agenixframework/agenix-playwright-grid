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

namespace Agenix.PlaywrightGrid.Shared.Execution.Logging;

/// <summary>
///     Represents the status of a logging scope.
/// </summary>
public enum LogScopeStatus
{
    /// <summary>
    ///     The logging scope is in progress.
    /// </summary>
    InProgress,

    /// <summary>
    ///     The logging scope has passed.
    /// </summary>
    Passed,

    /// <summary>
    ///     The logging scope has failed.
    /// </summary>
    Failed,

    /// <summary>
    ///     The logging scope has been skipped.
    /// </summary>
    Skipped,

    /// <summary>
    ///     The logging scope has a warning.
    /// </summary>
    Warn,

    /// <summary>
    ///     The logging scope has informational messages.
    /// </summary>
    Info
}
