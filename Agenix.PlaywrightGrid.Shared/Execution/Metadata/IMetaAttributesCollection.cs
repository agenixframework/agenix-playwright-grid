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

namespace Agenix.PlaywrightGrid.Shared.Execution.Metadata;

/// <summary>
///     Represents a collection of meta attributes.
/// </summary>
public interface IMetaAttributesCollection : ICollection<MetaAttribute>
{
    /// <summary>
    ///     Adds a new meta attribute with the specified key and value to the collection.
    /// </summary>
    /// <param name="key">The key of the meta attribute.</param>
    /// <param name="value">The value of the meta attribute.</param>
    void Add(string key, string value);
}
