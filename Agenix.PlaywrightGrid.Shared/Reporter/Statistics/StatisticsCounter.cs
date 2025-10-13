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
public class StatisticsCounter : IStatisticsCounter
{
    private readonly object _lockObj = new();

    private TimeSpan _sum;

    /// <inheritdoc />
    public TimeSpan Min { get; private set; }

    /// <inheritdoc />
    public TimeSpan Max { get; private set; }

    /// <inheritdoc />
    public TimeSpan Avg
    {
        get
        {
            lock (_lockObj)
            {
                return Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(_sum.Ticks / Count);
            }
        }
    }

    /// <inheritdoc />
    public long Count { get; private set; }

    /// <inheritdoc />
    public void Measure(TimeSpan duration)
    {
        lock (_lockObj)
        {
            if (Count == 0)
            {
                Min = duration;
                Max = duration;
                _sum = duration;
            }
            else
            {
                if (duration < Min)
                {
                    Min = duration;
                }
                else if (duration > Max)
                {
                    Max = duration;
                }

                _sum += duration;
            }

            Count++;
        }
    }

    /// <summary>
    ///     Returns a string that represents the statistics counter.
    /// </summary>
    /// <returns>A string that represents the statistics counter.</returns>
    public override string ToString()
    {
        return
            $"{Count} cnt min/avg/max {Min.TotalMilliseconds:F0}/{Avg.TotalMilliseconds:F0}/{Max.TotalMilliseconds:F0} ms";
    }
}
