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

namespace Agenix.PlaywrightGrid.Shared.Reporter.Statistics;

/// <inheritdoc />
public class LaunchStatisticsCounter : ILaunchStatisticsCounter
{
    /// <inheritdoc />
    public IStatisticsCounter StartTestItemStatisticsCounter { get; } = new StatisticsCounter();

    /// <inheritdoc />
    public IStatisticsCounter FinishTestItemStatisticsCounter { get; } = new StatisticsCounter();

    /// <inheritdoc />
    public IStatisticsCounter LogItemStatisticsCounter { get; } = new StatisticsCounter();

    /// <summary>
    ///     Returns a string that represents the statistics counter for launch.
    /// </summary>
    /// <returns>A string that represents the statistics counter.</returns>
    public override string ToString()
    {
        return
            $"ST {StartTestItemStatisticsCounter}, FT {FinishTestItemStatisticsCounter}, L {LogItemStatisticsCounter}";
    }
}
