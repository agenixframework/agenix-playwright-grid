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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Filtering;

/// <summary>
///     Represents the paging information for a collection of items.
/// </summary>
public class Paging
{
    /// <summary>
    ///     Initializes a new instance of the Paging class with the specified number and size.
    /// </summary>
    /// <param name="number">The page number.</param>
    /// <param name="size">The number of items per page.</param>
    public Paging(int number, int size)
    {
        Number = number;
        Size = size;
    }

    /// <summary>
    ///     Gets or sets the page number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    ///     Gets or sets the number of items per page.
    /// </summary>
    public int Size { get; set; }
}
