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

using System.Reflection;
using System.Text.Json;

namespace Agenix.PlaywrightGrid.Shared.Configuration.Providers;

/// <summary>
///     Parse json file with configuration properties as flatten dictionary.
/// </summary>
/// <remarks>
///     Creates new instance of <see cref="JsonFileConfigurationProvider" /> class.
/// </remarks>
/// <param name="delimeter">Char which represents hierarchy of flatten properties.</param>
/// <param name="filePath">The path to json file.</param>
/// <param name="optional">If file doesn't exist then empty disctionary will be returns.</param>
public class JsonFileConfigurationProvider(string delimeter, string filePath, bool optional) : IConfigurationProvider
{
    private readonly string _delimeter = delimeter ?? throw new ArgumentNullException(nameof(delimeter));
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    private readonly bool _optional = optional;

    static JsonFileConfigurationProvider()
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
    }

    /// <inheritdoc />
    public IDictionary<string, string> Load()
    {
        var properties = new Dictionary<string, string>();

        var directory = Path.GetDirectoryName(_filePath);

        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        var files = Directory.GetFiles(directory);

        var filePath = files.FirstOrDefault(f => f.Equals(_filePath, StringComparison.InvariantCultureIgnoreCase));

        if (filePath != null)
        {
            var json = File.ReadAllText(filePath);

            var flattenProperties = GetFlattenProperties(json);

            foreach (var property in flattenProperties)
            {
                properties[property.Key] = property.Value;
            }
        }
        else if (!_optional)
        {
            throw new FileNotFoundException($"Required configuration file '{_filePath}' was not found.", _filePath);
        }

        return properties;
    }

    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        if (args.Name.StartsWith("System.Text.Json", StringComparison.OrdinalIgnoreCase))
        {
            return Assembly.Load("System.Text.Json");
        }

        return null;
    }

    private Dictionary<string, string> GetFlattenProperties(string json)
    {
        var properties = new Dictionary<string, string>();

        try
        {
            using var jsonDocument = JsonDocument.Parse(json);
            foreach (var jsonProperty in jsonDocument.RootElement.EnumerateObject())
            {
                foreach (var item in ParseJsonProperty(jsonProperty))
                {
                    properties.Add(item.Key, item.Value);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Error parsing configuration file '{_filePath}'.", ex);
        }

        return properties;
    }

    private Dictionary<string, string> ParseJsonProperty(JsonProperty jsonProperty, string parentPropertyName = null)
    {
        return ParseJsonElement(jsonProperty.Value, string.IsNullOrEmpty(parentPropertyName) ? jsonProperty.Name : $"{parentPropertyName}{_delimeter}{jsonProperty.Name}");
    }

    private Dictionary<string, string> ParseJsonElement(JsonElement element, string propertyName)
    {
        var properties = new Dictionary<string, string>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var innerJsonProperty in element.EnumerateObject())
            {
                foreach (var item in ParseJsonProperty(innerJsonProperty, propertyName))
                {
                    properties.Add(item.Key, item.Value);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                var itemPropertyName = $"{propertyName}{_delimeter}{index}";
                foreach (var nestedProperty in ParseJsonElement(item, itemPropertyName))
                {
                    properties.Add(nestedProperty.Key, nestedProperty.Value);
                }
                index++;
            }
        }
        else if (element.ValueKind != JsonValueKind.Undefined && element.ValueKind != JsonValueKind.Null)
        {
            properties.Add(propertyName, element.ToString());
        }
        else if (element.ValueKind == JsonValueKind.Null)
        {
            properties.Add(propertyName, string.Empty);
        }

        return properties;
    }
}
