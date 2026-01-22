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

namespace Agenix.PlaywrightGrid.Client.Extensions;

internal static class UriExtensions
{
    public static Uri Normalize(this Uri uri)
    {
        var normalizedUriString = $"{uri.Scheme}://{uri.Authority}";

        for (var i = 0; i < uri.Segments.Length; i++)
        {
            if (!uri.Segments[i].Equals("v1/", StringComparison.OrdinalIgnoreCase) &&
                !uri.Segments[i].Equals("v1", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUriString += uri.Segments[i];
            }
        }

        normalizedUriString = normalizedUriString.TrimEnd('/');

        if (!normalizedUriString.EndsWith("api", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUriString += "/api";
        }

        if (!normalizedUriString.EndsWith("/"))
        {
            normalizedUriString += '/';
        }

        return new Uri(normalizedUriString);
    }
}
