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

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Agenix.PlaywrightGrid.Shared.Reporter.Statistics;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <summary>
///     Base class to expose functionality to execute a function (request) with statistics measuring.
/// </summary>
public abstract class BaseRequestExecutor : IRequestExecutor
{
    /// <inheritdoc />
    public virtual async Task<T> ExecuteAsync<T>(Func<Task<T>> func, Action<Exception> beforeNextAttemptCallback,
        IStatisticsCounter statisticsCounter, [CallerMemberName] string logicalOperationName = null)
    {
        ArgumentNullException.ThrowIfNull(func);

        var sw = Stopwatch.StartNew();

        try
        {
            return await func();
        }
        finally
        {
            sw.Stop();

            statisticsCounter?.Measure(sw.Elapsed);
        }
    }
}
