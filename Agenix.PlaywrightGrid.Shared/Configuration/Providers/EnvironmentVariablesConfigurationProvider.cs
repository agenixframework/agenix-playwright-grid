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

using System.Collections;

namespace Agenix.PlaywrightGrid.Shared.Configuration.Providers;

/// <summary>
///     Retrieves environment variables as configuration properties.
/// </summary>
/// <remarks>
///     Creates new instance of <see cref="EnvironmentVariablesConfigurationProvider" /> class.
/// </remarks>
/// <param name="prefix">Only use environment variables which starts from spicific prefix.</param>
/// <param name="delimeter">Property is considered as hierarchical if its name contains specific character.</param>
/// <param name="target">Environment variables scope, like machine scoped or process scoped.</param>
public class EnvironmentVariablesConfigurationProvider(string prefix, string delimeter, EnvironmentVariableTarget target) : IConfigurationProvider
{
    private readonly string _delimeter = delimeter ?? string.Empty;
    private readonly string _prefix = prefix ?? string.Empty;
    private readonly EnvironmentVariableTarget _target = target;

    /// <inheritdoc />
    public IDictionary<string, string> Load()
    {
        var properties = new Dictionary<string, string>();

        var variables = Environment.GetEnvironmentVariables(_target).Cast<DictionaryEntry>()
            .Where(v => ((string)v.Key).StartsWith(_prefix, StringComparison.OrdinalIgnoreCase));

        foreach (var variable in variables)
        {
            properties[
                    ((string)variable.Key)[_prefix.Length..]
                    .Replace(_delimeter, ConfigurationPath.KeyDelimeter)] =
                (string)variable.Value;
        }

        return properties;
    }
}
