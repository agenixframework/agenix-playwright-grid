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

using System.Text;
using System.Text.RegularExpressions;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Embedded.Analytics;

// TODO: Makethis class testable, or even create GlobalConfiguration
internal static partial class ClientIdProvider
{
    private const string CLIENT_ID_KEY = "client.id";

    public static readonly string FILE_PATH =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rp", "rp.properties");

    /// <summary>
    ///     Asynchronously gets the client ID from the properties file.
    ///     If the file does not exist or the client ID is not found, a new ID is generated and saved to the file.
    /// </summary>
    /// <returns>The client ID as a string.</returns>
    public static async Task<string> GetClientIdAsync()
    {
        var clientId = await ReadClientIdAsync();

        if (string.IsNullOrEmpty(clientId))
        {
            clientId = Guid.NewGuid().ToString();
            await SaveClientIdAsync(clientId);
        }

        return clientId;
    }

    private static async Task<string> ReadClientIdAsync()
    {
        if (File.Exists(FILE_PATH))
        {
            using var reader = new StreamReader(FILE_PATH);
            var contents = await reader.ReadToEndAsync();
            var matches = MyRegex().Matches(contents);
            if (matches.Count > 0)
            {
                return matches[0].Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static async Task SaveClientIdAsync(string clientId)
    {
        var contents = new StringBuilder();
        if (File.Exists(FILE_PATH))
        {
            using var reader = new StreamReader(FILE_PATH);
            contents.Append(await reader.ReadToEndAsync());
            if (contents.Length > 0 && !contents.ToString().EndsWith("\n"))
            {
                contents.Append('\n');
            }
        }

        contents.Append($"{CLIENT_ID_KEY} = {clientId}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(FILE_PATH)); // Ensure the directory exists
        using var writer = new StreamWriter(FILE_PATH);
        await writer.WriteAsync(contents.ToString());
    }

    [GeneratedRegex(@"client.id\s*=\s*(\S*)")]
    private static partial Regex MyRegex();
}
