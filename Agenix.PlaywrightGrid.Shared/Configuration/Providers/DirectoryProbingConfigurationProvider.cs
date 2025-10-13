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

using System.Text.RegularExpressions;

namespace Agenix.PlaywrightGrid.Shared.Configuration.Providers;

/// <summary>
///     Finds files in a directory and consider their content as a value for configuration properties.
/// </summary>
/// <remarks>
///     Creates new instance of <see cref="DirectoryProbingConfigurationProvider" /> class.
/// </remarks>
/// <param name="directoryPath">Path to a directory where to find files.</param>
/// <param name="prefix">Limit files searching.</param>
/// <param name="delimeter">Consider this string as hierarchic property.</param>
/// <param name="optional">Returns empty list of properties if directory doesn't exist.</param>
public class DirectoryProbingConfigurationProvider(string directoryPath, string prefix, string delimeter, bool optional) : IConfigurationProvider
{
    private readonly string _delimeter = delimeter ?? throw new ArgumentNullException(nameof(delimeter));
    private readonly string _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
    private readonly bool _optional = optional;
    private readonly string _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));

    /// <inheritdoc />
    public IDictionary<string, string> Load()
    {
        var properties = new Dictionary<string, string>();

        if (Directory.Exists(_directoryPath))
        {
            var directory = new DirectoryInfo(_directoryPath);

            var escapedDelimeter = Regex.Escape(_delimeter);
            var pattern = $"{_prefix.ToLowerInvariant()}({escapedDelimeter}[a-zA-Z]+)+";

            var ignoredFileExtensions = new[] { ".exe", ".dll", ".pdb", ".log" };

            var candidates = directory.EnumerateFiles().Where(f =>
                Regex.IsMatch(f.Name.ToLowerInvariant(), pattern) &&
                !ignoredFileExtensions.Contains(f.Extension.ToLowerInvariant()));

            foreach (var candidate in candidates)
            {
                var key = candidate.Name.ToLowerInvariant()
                    .Replace($"{_prefix.ToLowerInvariant()}{_delimeter}", string.Empty)
                    .Replace(_delimeter, ConfigurationPath.KeyDelimeter);
                var value = File.ReadAllText(candidate.FullName);

                properties[key] = value.Trim();
            }
        }
        else
        {
            if (!_optional)
            {
                throw new DirectoryNotFoundException(
                    $"Required directory not found by '{_directoryPath}' path as configuration provider.");
            }
        }

        return properties;
    }
}
