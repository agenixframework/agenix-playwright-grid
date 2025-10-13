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

namespace Agenix.PlaywrightGrid.Shared.Configuration;

/// <inheritdoc />
/// <summary>
///     Creates new instance of <see cref="Configuration" /> class and provide a way to retrieve properties.
/// </summary>
/// <param name="values"></param>
public class Configuration(IDictionary<string, object> values) : IConfiguration
{
    private readonly string _notFoundMessage =
        "Property '{0}' not found in the configuration. Make sure you have configured it properly.";

    /// <inheritdoc />
    public IDictionary<string, object> Properties { get; } = values;

    private static readonly string[] separator = new[] { ";" };

    /// <inheritdoc />
    public T GetValue<T>(string property)
    {
        if (!Properties.TryGetValue(property, out object? value))
        {
            throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, _notFoundMessage, property));
        }

        var propertyValue = value;

        return ConvertValue<T>(propertyValue);
    }

    /// <inheritdoc />
    public T GetValue<T>(string property, T defaultValue)
    {
        if (!Properties.ContainsKey(property))
        {
            return defaultValue;
        }

        return GetValue<T>(property);
    }

    /// <inheritdoc />
    public IEnumerable<T> GetValues<T>(string property)
    {
        var values = new List<string>();

        if (Properties.TryGetValue(property, out var propertyValue))
        {
            values.AddRange((propertyValue.ToString() ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries));
        }

        // Also look for indexed properties (e.g., property:0, property:1)
        var indexedPrefix = $"{property}:";
        var indexedValues = Properties
            .Where(p => p.Key.StartsWith(indexedPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key.Length)
            .ThenBy(p => p.Key)
            .Select(p => p.Value.ToString() ?? string.Empty);

        values.AddRange(indexedValues);

        if (values.Count == 0)
        {
            throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, _notFoundMessage, property));
        }

        return values.Select(v => ConvertValue<T>(v)).ToList();
    }

    /// <inheritdoc />
    public IEnumerable<T> GetValues<T>(string property, IEnumerable<T> defaultValue)
    {
        if (!Properties.ContainsKey(property))
        {
            return defaultValue;
        }

        return GetValues<T>(property);
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, T>> GetKeyValues<T>(string property)
    {
        var result = new List<KeyValuePair<string, T>>();

        // Handle wildcard (e.g., Server:*)
        if (property.EndsWith("*"))
        {
            var prefix = property[..^1];
            var matches = Properties
                .Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => new KeyValuePair<string, T>(p.Key, ConvertValue<T>(p.Value)));

            result.AddRange(matches);
        }
        else
        {
            if (Properties.TryGetValue(property, out var propertyValue))
            {
                var values = (propertyValue.ToString() ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var value in values)
                {
                    var entries = value.Split(':');

                    string key;
                    string keyValue;

                    if (entries.Length == 1)
                    {
                        key = string.Empty;
                        keyValue = value;
                    }
                    else
                    {
                        key = entries[0];
                        keyValue = value[(key.Length + 1)..];
                    }

                    result.Add(new KeyValuePair<string, T>(key.Trim(), ConvertValue<T>(keyValue.Trim())));
                }
            }

            // Also look for indexed properties (e.g., property:0, property:1)
            var indexedPrefix = $"{property}:";
            var indexedMatches = Properties
                .Where(p => p.Key.StartsWith(indexedPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Key.Length)
                .ThenBy(p => p.Key)
                .Select(p =>
                {
                    var value = p.Value.ToString() ?? string.Empty;
                    var entries = value.Split(':');
                    if (entries.Length > 1)
                    {
                        var key = entries[0];
                        var val = value[(key.Length + 1)..];
                        return new KeyValuePair<string, T>(key.Trim(), ConvertValue<T>(val.Trim()));
                    }
                    return new KeyValuePair<string, T>(string.Empty, ConvertValue<T>(value));
                });

            result.AddRange(indexedMatches);
        }

        if (result.Count == 0)
        {
            throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, _notFoundMessage, property));
        }

        return result;
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, T>> GetKeyValues<T>(string property,
        IEnumerable<KeyValuePair<string, T>> defaultValue)
    {
        if (!Properties.ContainsKey(property))
        {
            return defaultValue;
        }

        return GetKeyValues<T>(property);
    }

    private static T ConvertValue<T>(object value)
    {
        if (typeof(T) == typeof(bool))
        {
            var trueValues = new List<string> { "true", "yes", "y", "1" };
            var falseValues = new List<string> { "false", "no", "n", "0" };

            if (trueValues.Any(v => value.ToString().Equals(v, StringComparison.InvariantCultureIgnoreCase)))
            {
                return (T)(object)true;
            }

            if (falseValues.Any(v => value.ToString().Equals(v, StringComparison.InvariantCultureIgnoreCase)))
            {
                return (T)(object)false;
            }

            throw new InvalidCastException($"Unknown '{value}' value for '{typeof(T)}'.");
        }

        if (typeof(T).IsEnum)
        {
            return (T)Enum.Parse(typeof(T), value.ToString(), true);
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }
}
