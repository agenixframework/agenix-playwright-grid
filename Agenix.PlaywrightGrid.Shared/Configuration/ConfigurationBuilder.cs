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

namespace Agenix.PlaywrightGrid.Shared.Configuration;

/// <inheritdoc />
public class ConfigurationBuilder : IConfigurationBuilder
{
    /// <inheritdoc />
    public IList<IConfigurationProvider> Providers { get; } = [];

    /// <inheritdoc />
    public IConfigurationBuilder Add(IConfigurationProvider provider)
    {
        Providers.Add(provider);

        return this;
    }

    /// <inheritdoc />
    public IConfiguration Build()
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in Providers)
        {
            var originalProperties = provider.Load();

            foreach (var property in originalProperties)
            {
                if (property.Value.StartsWith(ConfigurationPath.AppenderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (properties.ContainsKey(property.Key))
                    {
                        properties[property.Key] += property.Value[ConfigurationPath.AppenderPrefix.Length..];
                    }
                    else
                    {
                        properties[property.Key] = property.Value[ConfigurationPath.AppenderPrefix.Length..];
                    }
                }
                else
                {
                    properties[property.Key] = property.Value;
                }
            }
        }

        return new Configuration(properties);
    }
}
