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

namespace Agenix.PlaywrightGrid.Shared.Extensibility;

/// <summary>
///     Represents an interface for managing extensions.
/// </summary>
public interface IExtensionManager
{
    /// <summary>
    ///     Gets the list of report event observers.
    /// </summary>
    IList<IReportEventsObserver> ReportEventObservers { get; }

    /// <summary>
    ///     Gets the list of commands listeners.
    /// </summary>
    IList<ICommandsListener> CommandsListeners { get; }

    /// <summary>
    ///     Explores the specified path for extensions.
    /// </summary>
    /// <param name="path">The path to explore.</param>
    void Explore(string path);
}
