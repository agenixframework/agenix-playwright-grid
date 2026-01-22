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
///     Stores configuration variables from different providers.
/// </summary>
public interface IConfiguration
{
    /// <summary>
    ///     Fetched configuration variables.
    /// </summary>
    IDictionary<string, object> Properties { get; }

    /// <summary>
    ///     Returns value of configuration property.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="property"></param>
    /// <returns></returns>
    T GetValue<T>(string property);

    /// <summary>
    ///     Returns value of configuration property.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    T GetValue<T>(string property, T defaultValue);

    /// <summary>
    ///     Returns values of configuration property.
    /// </summary>
    /// <param name="property"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IEnumerable<T> GetValues<T>(string property);

    /// <summary>
    ///     Returns values of configuration property.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IEnumerable<T> GetValues<T>(string property, IEnumerable<T> defaultValue);

    /// <summary>
    ///     Returns pairs of key:value of configuration property.
    /// </summary>
    /// <param name="property"></param>
    /// <returns></returns>
    IEnumerable<KeyValuePair<string, T>> GetKeyValues<T>(string property);

    /// <summary>
    ///     Returns pairs of key:value of configuration property.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="defaultValue"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IEnumerable<KeyValuePair<string, T>> GetKeyValues<T>(string property,
        IEnumerable<KeyValuePair<string, T>> defaultValue);
}
