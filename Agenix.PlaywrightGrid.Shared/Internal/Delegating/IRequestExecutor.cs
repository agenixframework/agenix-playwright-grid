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

using System.Runtime.CompilerServices;
using Agenix.PlaywrightGrid.Shared.Reporter.Statistics;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <summary>
///     Delegate to invoke any Func.
/// </summary>
public interface IRequestExecutor
{
    /// <summary>
    ///     Executes func.
    /// </summary>
    /// <param name="func">Function for execution.</param>
    /// <param name="beforeNextAttemptCallback">Callback action to be invoked between attempts.</param>
    /// <param name="statisticsCounter">Statistics counter to capture requests duration.</param>
    /// <param name="logicalOperationName">Logical operation name which describes the function to be invoked.</param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T> ExecuteAsync<T>(Func<Task<T>> func, Action<Exception> beforeNextAttemptCallback,
        IStatisticsCounter statisticsCounter, [CallerMemberName] string logicalOperationName = null);
}
