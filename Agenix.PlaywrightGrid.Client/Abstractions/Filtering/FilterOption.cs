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

using System.Globalization;
using System.Text;
using Agenix.PlaywrightGrid.Client.Extensions;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Filtering;

public class FilterOption
{
    public Paging Paging { get; set; }
    public Sorting Sorting { get; set; }
    public List<Filter> Filters { get; set; }

    public override string ToString()
    {
        var builder = new StringBuilder();

        if (Paging != null)
        {
            builder.Append($"page.page={Paging.Number.ToString(CultureInfo.InvariantCulture)}");
            builder.Append($"&page.size={Paging.Size.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Sorting != null)
        {
            builder.Append(
                $"&page.sort={string.Join(",", Sorting.Fields.ToArray()) + "," + Sorting.Direction.GetDescriptionAttribute()}");
        }

        if (Filters != null)
        {
            foreach (var filter in Filters)
            {
                var value = string.Join(",", filter.Values.Select(s => s.ToString()).ToArray());
                builder.Append($"&filter.{filter.Operation.GetDescriptionAttribute()}.{filter.Field}={value}");
            }
        }

        return builder.ToString();
    }
}
