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

namespace Agenix.PlaywrightGrid.Shared.Configuration.Providers;

/// <summary>
///     Configuration provider that loads from an in-memory dictionary.
/// </summary>
/// <remarks>
///     Creates a new in-memory configuration provider.
/// </remarks>
/// <param name="values">Dictionary of configuration key-value pairs</param>
public sealed class InMemoryConfigurationProvider(IDictionary<string, object> values) : IConfigurationProvider
{
    private readonly IDictionary<string, object> _values = values ?? throw new ArgumentNullException(nameof(values));

    /// <inheritdoc />
    public IDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _values)
        {
            result[kvp.Key] = kvp.Value switch
            {
                null => string.Empty,
                IConvertible convertible => convertible.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => kvp.Value.ToString() ?? string.Empty
            };
        }

        return result;
    }
}
