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

using IAsyncLaunchResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncLaunchResource;
using IAsyncLogItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncLogItemResource;
using IAsyncTestItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncTestItemResource;
using ILaunchResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ILaunchResource;
using ILogItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ILogItemResource;
using IProjectResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IProjectResource;
using ITestItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ITestItemResource;
using IUserFilterResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IUserFilterResource;
using IUserResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IUserResource;

namespace ReportPortal.Client.Abstractions;

/// <summary>
///     Interface to interact with common Report Portal services. Provides possibility to manage almost of service's
///     endpoints.
/// </summary>
public interface IClientService
{
    /// <summary>
    ///     Gets the resource for managing launches.
    /// </summary>
    ILaunchResource Launch { get; }

    /// <summary>
    ///     Gets the resource for managing asynchronous launches.
    /// </summary>
    IAsyncLaunchResource AsyncLaunch { get; }

    /// <summary>
    ///     Gets the resource for managing test items.
    /// </summary>
    ITestItemResource TestItem { get; }

    /// <summary>
    ///     Gets the resource for managing asynchronous test items.
    /// </summary>
    IAsyncTestItemResource AsyncTestItem { get; }

    /// <summary>
    ///     Gets the resource for managing log items.
    /// </summary>
    ILogItemResource LogItem { get; }

    /// <summary>
    ///     Gets the resource for managing asynchronous log items.
    /// </summary>
    IAsyncLogItemResource AsyncLogItem { get; }

    /// <summary>
    ///     Gets the resource for managing users.
    /// </summary>
    IUserResource User { get; }

    /// <summary>
    ///     Gets the resource for managing user filters.
    /// </summary>
    IUserFilterResource UserFilter { get; }

    /// <summary>
    ///     Gets the resource for managing projects.
    /// </summary>
    IProjectResource Project { get; }
}
