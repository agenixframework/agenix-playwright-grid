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

using Agenix.PlaywrightGrid.Client.Abstractions.Filtering;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Resources;

/// <summary>
///     Interacts with user filter resources.
/// </summary>
public interface IUserFilterResource
{
    /// <summary>
    ///     Creates a new user filter.
    /// </summary>
    Task<UserFilterCreatedResponse> CreateAsync(CreateUserFilterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing user filter.
    /// </summary>
    Task<MessageResponse> UpdateAsync(long id, UpdateUserFilterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all user filters.
    /// </summary>
    Task<Content<UserFilterResponse>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets user filters with filtering options.
    /// </summary>
    Task<Content<UserFilterResponse>>
        GetAsync(FilterOption filterOption, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a specific user filter by ID.
    /// </summary>
    Task<UserFilterResponse> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a user filter by ID.
    /// </summary>
    Task<MessageResponse> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
