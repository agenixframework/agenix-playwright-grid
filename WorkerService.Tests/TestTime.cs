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

namespace WorkerService.Tests;

/// <summary>
/// Deterministic time utilities for tests to avoid flakiness due to clock resolution or scheduling.
/// </summary>
public static class TestTime
{
    /// <summary>
    /// Returns a UTC DateTime with specified components.
    /// </summary>
    public static DateTime FixedUtc(int year = 2025, int month = 1, int day = 1, int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
        => new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
}
