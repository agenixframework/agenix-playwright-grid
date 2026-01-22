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

using Agenix.PlaywrightGrid.Shared.Configuration.Providers;

namespace Agenix.PlaywrightGrid.Shared.Configuration;

/// <summary>
///     Provides extension methods for IConfigurationBuilder.
/// </summary>
public static class ConfigurationBuilderExtensions
{
    /// <summary>
    ///     Adds default configuration sources to the configuration builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddDefaults(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var baseDir = Environment.CurrentDirectory;

        return builder.AddDefaults(baseDir);
    }

    /// <summary>
    ///     Adds default configuration sources to the configuration builder with a specified base directory.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="baseDir">The base directory.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddDefaults(this IConfigurationBuilder builder, string baseDir)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddJsonFile(Path.Combine(baseDir, "ReportPortal.json"));
        builder.AddJsonFile(Path.Combine(baseDir, "ReportPortal.config.json"));
        builder.AddDirectoryProbing(baseDir);
        builder.AddEnvironmentVariables();

        return builder;
    }

    /// <summary>
    ///     Adds a JSON file as a configuration source to the configuration builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="filePath">The path to the JSON file.</param>
    /// <param name="optional">A flag indicating whether the file is optional.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddJsonFile(this IConfigurationBuilder builder, string filePath,
        bool optional = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new JsonFileConfigurationProvider(ConfigurationPath.KeyDelimeter, filePath, optional));
    }

    /// <summary>
    ///     Adds environment variables as a configuration source to the configuration builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddEnvironmentVariables(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEnvironmentVariables("RP_");
        builder.AddEnvironmentVariables("RP__", "__");

        builder.AddEnvironmentVariables("REPORTPORTAL_");
        builder.AddEnvironmentVariables("REPORTPORTAL__", "__");

        return builder;
    }

    /// <summary>
    ///     Adds environment variables with a specified prefix and delimiter as a configuration source to the configuration
    ///     builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="prefix">The prefix for the environment variables.</param>
    /// <param name="delimiter">The delimiter for the environment variables.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddEnvironmentVariables(this IConfigurationBuilder builder,
        string prefix = "REPORTPORTAL_", string delimiter = "_")
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(
            new EnvironmentVariablesConfigurationProvider(prefix, delimiter, EnvironmentVariableTarget.Machine));

        builder.Add(new EnvironmentVariablesConfigurationProvider(prefix, delimiter, EnvironmentVariableTarget.User));

        builder.Add(
            new EnvironmentVariablesConfigurationProvider(prefix, delimiter, EnvironmentVariableTarget.Process));

        return builder;
    }

    /// <summary>
    ///     Adds a directory probing configuration provider to the configuration builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <param name="prefix">The prefix for the configuration keys.</param>
    /// <param name="delimiter">The delimiter for the configuration keys.</param>
    /// <param name="optional">A flag indicating whether the directory is optional.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddDirectoryProbing(this IConfigurationBuilder builder, string directoryPath,
        string prefix = "ReportPortal", string delimiter = "_", bool optional = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(new DirectoryProbingConfigurationProvider(directoryPath, prefix, delimiter, optional));

        return builder;
    }

    /// <summary>
    ///     Adds an in-memory configuration provider to the configuration builder.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="values">Dictionary of configuration key-value pairs.</param>
    /// <returns>The configuration builder.</returns>
    public static IConfigurationBuilder AddInMemory(this IConfigurationBuilder builder,
        IDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(new InMemoryConfigurationProvider(values));

        return builder;
    }
}
