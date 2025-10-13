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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;

namespace Agenix.PlaywrightGrid.Shared.Converters;

/// <summary>
///     Converter of any string to <see cref="ItemAttribute" /> instance.
/// </summary>
public class ItemAttributeConverter
{
    private static readonly char[] separator = new[] { ':' };

    /// <summary>
    ///     Translate string to ItemAttribute
    ///     component:search =>     key=component, value=search
    ///     :search                 key=, value=search
    ///     search:                 key=, value=search
    ///     Attribute value always should not be empty.
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="optionsProvider"></param>
    /// <returns></returns>
    public static ItemAttribute ConvertFrom(string tag, Action<ConvertOptions> optionsProvider = null)
    {
        var options = ConvertOptions.Default;

        optionsProvider?.Invoke(options);

        var attr = new ItemAttribute();

        var values = tag.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        if (values.Length == 1 || string.IsNullOrEmpty(values[1]))
        {
            attr.Key = options.UndefinedKey;
            attr.Value = values[0];
        }
        else
        {
            attr.Key = values[0];
            attr.Value = tag[(values[0].Length + 1)..];
        }

        return attr;
    }

    /// <summary>
    ///     Defines options for <see cref="ItemAttributeConverter" />.
    /// </summary>
    public class ConvertOptions
    {
        /// <summary>
        ///     Key if it was not parsed.
        /// </summary>
        public string UndefinedKey { get; set; }

        /// <summary>
        ///     Returns default converter options.
        /// </summary>
        public static ConvertOptions Default => new();
    }
}
