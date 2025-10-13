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

namespace Dashboard.Application;

/// <summary>
///     Per-user avatar state shared across Dashboard components in a Blazor Server circuit.
///     Allows Profile page to push updates so Sidebar and menus refresh immediately.
/// </summary>
public sealed class AvatarState
{
    /// <summary>Current avatar URL (can be a data: URL).</summary>
    public string Url { get; private set; } = "images/avatar.svg";

    /// <summary>Raised when avatar URL changes.</summary>
    public event Action? Changed;

    /// <summary>Update avatar URL and notify listeners.</summary>
    public void Set(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "images/avatar.svg";
        }

        if (!string.Equals(Url, url, StringComparison.Ordinal))
        {
            Url = url;
            try { Changed?.Invoke(); }
            catch { }
        }
    }
}
