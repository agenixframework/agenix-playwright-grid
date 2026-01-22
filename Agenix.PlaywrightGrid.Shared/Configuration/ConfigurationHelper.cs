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

/// <summary>
///     Helper methods for working with PlaywrightGrid configuration.
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    ///     Gets the hub server URL with fallback to legacy key.
    /// </summary>
    public static string GetServerUrl(IConfiguration config)
    {
        try
        {
            return config.GetValue<string>(ConfigurationKeys.ServerUrl);
        }
        catch (KeyNotFoundException)
        {
            try
            {
                return config.GetValue<string>(ConfigurationKeys.HubUrl);
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException(
                    "Server URL not configured. Set 'server.url' in PlaywrightGrid.json or 'PlaywrightGrid:HubUrl' in environment.");
            }
        }
    }

    /// <summary>
    ///     Gets the project key with fallback to legacy key.
    /// </summary>
    public static string GetProjectKey(IConfiguration config)
    {
        try
        {
            return config.GetValue<string>(ConfigurationKeys.ServerProject);
        }
        catch (KeyNotFoundException)
        {
            try
            {
                return config.GetValue<string>(ConfigurationKeys.ProjectKey);
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException(
                    "Project key not configured. Set 'server.project' in PlaywrightGrid.json or 'PlaywrightGrid:ProjectKey' in environment.");
            }
        }
    }

    /// <summary>
    ///     Gets the API key with fallback to legacy key.
    /// </summary>
    public static string? GetApiKey(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.ServerApiKey, (string?)null)
               ?? config.GetValue(ConfigurationKeys.ApiKey, (string?)null);
    }

    /// <summary>
    ///     Gets whether PlaywrightGrid is enabled (default: true).
    /// </summary>
    public static bool IsEnabled(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.Enabled, true);
    }

    /// <summary>
    ///     Gets the default label key for test items with fallback to legacy key.
    /// </summary>
    public static string? GetDefaultLabelKey(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.TestItemDefaultLabelKey, (string?)null)
               ?? config.GetValue(ConfigurationKeys.DefaultLabelKey, (string?)null);
    }

    /// <summary>
    ///     Gets the default launch name from configuration.
    /// </summary>
    public static string? GetLaunchName(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.LaunchName, (string?)null);
    }

    /// <summary>
    ///     Gets the default launch description from configuration.
    /// </summary>
    public static string? GetLaunchDescription(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.LaunchDescription, (string?)null);
    }

    /// <summary>
    ///     Gets the launch attributes array from configuration.
    /// </summary>
    public static string[] GetLaunchAttributes(IConfiguration config)
    {
        var attributes = new List<string>();
        var index = 0;

        while (true)
        {
            var key = $"{ConfigurationKeys.LaunchAttributes}:{index}";
            var value = config.GetValue(key, (string?)null);

            if (string.IsNullOrEmpty(value))
            {
                break;
            }

            attributes.Add(value);
            index++;
        }

        return [.. attributes];
    }

    /// <summary>
    ///     Gets the default suite name from configuration.
    /// </summary>
    public static string? GetSuiteName(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.SuiteName, (string?)null);
    }

    /// <summary>
    ///     Gets the default suite description from configuration.
    /// </summary>
    public static string? GetSuiteDescription(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.SuiteDescription, (string?)null);
    }

    /// <summary>
    ///     Gets the suite attributes array from configuration.
    /// </summary>
    public static string[] GetSuiteAttributes(IConfiguration config)
    {
        var attributes = new List<string>();
        var index = 0;

        while (true)
        {
            var key = $"{ConfigurationKeys.SuiteAttributes}:{index}";
            var value = config.GetValue(key, (string?)null);

            if (string.IsNullOrEmpty(value))
            {
                break;
            }

            attributes.Add(value);
            index++;
        }

        return [.. attributes];
    }

    /// <summary>
    ///     Gets the test item attributes array from configuration.
    /// </summary>
    public static string[] GetTestItemAttributes(IConfiguration config)
    {
        var attributes = new List<string>();
        var index = 0;

        while (true)
        {
            var key = $"{ConfigurationKeys.TestItemAttributes}:{index}";
            var value = config.GetValue(key, (string?)null);

            if (string.IsNullOrEmpty(value))
            {
                break;
            }

            attributes.Add(value);
            index++;
        }

        return [.. attributes];
    }

    /// <summary>
    ///     Gets the HTTP timeout in seconds (default: 30).
    ///     Supports both legacy and new configuration keys.
    /// </summary>
    public static int GetTimeoutSeconds(IConfiguration config)
    {
        // Try new structured key first
        var timeout = config.GetValue(ConfigurationKeys.TimeoutConfigSeconds, 0);
        if (timeout > 0)
        {
            return timeout;
        }

        // Fallback to legacy key
        return config.GetValue(ConfigurationKeys.TimeoutSeconds, 30);
    }

    /// <summary>
    ///     Gets the retry count (default: 3).
    ///     Supports both legacy and new configuration keys.
    /// </summary>
    public static int GetRetryCount(IConfiguration config)
    {
        // Try new structured key first
        var retryCount = config.GetValue(ConfigurationKeys.RetryConfigCount, 0);
        if (retryCount > 0)
        {
            return retryCount;
        }

        // Fallback to legacy key
        return config.GetValue(ConfigurationKeys.RetryCount, 3);
    }

    /// <summary>
    ///     Gets the retry delay in seconds (default: 2).
    /// </summary>
    public static int GetRetryDelaySeconds(IConfiguration config)
    {
        return config.GetValue(ConfigurationKeys.RetryDelaySeconds, 2);
    }

    /// <summary>
    ///     Gets the max concurrent requests (default: 10).
    ///     Supports both legacy and new configuration keys.
    /// </summary>
    public static int GetMaxConcurrency(IConfiguration config)
    {
        // Try new structured key first
        var maxConcurrent = config.GetValue(ConfigurationKeys.ConcurrencyMaxRequests, 0);
        if (maxConcurrent > 0)
        {
            return maxConcurrent;
        }

        // Fallback to legacy key
        return config.GetValue(ConfigurationKeys.MaxConcurrency, 10);
    }

    /// <summary>
    ///     Builds a configuration from a PlaywrightGrid.json file.
    /// </summary>
    /// <param name="filePath">Path to PlaywrightGrid.json (default: "./PlaywrightGrid.json")</param>
    /// <param name="optional">If true, missing file will not throw an exception</param>
    /// <returns>IConfiguration instance</returns>
    public static IConfiguration FromJsonFile(string filePath = "./PlaywrightGrid.json", bool optional = true)
    {
        return new ConfigurationBuilder()
            .AddJsonFile(filePath, optional)
            .AddEnvironmentVariables("PLAYWRIGHTGRID_")
            .Build();
    }

    /// <summary>
    ///     Builds a configuration from multiple sources with priority:
    ///     1. Environment variables (highest priority)
    ///     2. JSON file
    ///     3. In-memory defaults (lowest priority)
    /// </summary>
    public static IConfiguration FromMultipleSources(
        string? jsonFilePath = null,
        IDictionary<string, object>? defaults = null)
    {
        var builder = new ConfigurationBuilder();

        // Add defaults first (lowest priority)
        if (defaults != null)
        {
            builder.AddInMemory(defaults);
        }

        // Add JSON file second
        if (!string.IsNullOrEmpty(jsonFilePath))
        {
            builder.AddJsonFile(jsonFilePath, true);
        }
        else
        {
            builder.AddJsonFile("./PlaywrightGrid.json", true);
        }

        // Add environment variables last (highest priority)
        builder.AddEnvironmentVariables("PLAYWRIGHTGRID_");

        return builder.Build();
    }
}
