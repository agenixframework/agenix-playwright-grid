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

using System.Reflection;
using Agenix.PlaywrightGrid.Client.Extensions;
using Agenix.PlaywrightGrid.Client.Resources;
using ReportPortal.Client.Abstractions;
using IAsyncLaunchResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncLaunchResource;
using IAsyncLogItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncLogItemResource;
using IAsyncTestItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IAsyncTestItemResource;
using IHttpClientFactory = Agenix.PlaywrightGrid.Client.IHttpClientFactory;
using ILaunchResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ILaunchResource;
using ILogItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ILogItemResource;
using IProjectResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IProjectResource;
using ITestItemResource = Agenix.PlaywrightGrid.Client.Abstractions.Requests.ITestItemResource;
using IUserFilterResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IUserFilterResource;
using IUserResource = Agenix.PlaywrightGrid.Client.Abstractions.Resources.IUserResource;

namespace ReportPortal.Client;

/// <inheritdoc cref="IClientService" />
public partial class Service : IClientService, IDisposable
{
    private readonly HttpClient _httpClient;

    static Service()
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
    }

    /// <summary>
    ///     Constructor to initialize a new object of service.
    /// </summary>
    /// <param name="uri">Base URI for REST service.</param>
    /// <param name="projectName">A project to manage.</param>
    /// <param name="token">A token for user. Can be UID given from user's profile page.</param>
    /// <param name="httpClientFactory">Factory object to create an instance of <see cref="HttpClient" />.</param>
    public Service(Uri uri, string projectName, string token, IHttpClientFactory httpClientFactory = null)
    {
        ProjectName = projectName;

        if (httpClientFactory == null)
        {
            httpClientFactory = new HttpClientFactory(uri, token);
        }

        _httpClient = httpClientFactory.Create();
        _httpClient.BaseAddress = _httpClient.BaseAddress?.Normalize();

        Launch = new LaunchResource(_httpClient, ProjectName);
        AsyncLaunch = new ServiceAsyncLaunchResource(_httpClient, ProjectName);
        TestItem = new TestItemResource(_httpClient, ProjectName);
        AsyncTestItem = new ServiceAsyncTestItemResource(_httpClient, ProjectName);
        LogItem = new ServiceAsyncLogItemResource(_httpClient, ProjectName);
        AsyncLogItem = new ServiceAsyncLogItemResource(_httpClient, ProjectName);
        User = new ServiceUserResource(_httpClient, ProjectName);
        UserFilter = new ServiceUserFilterResource(_httpClient, ProjectName);
        Project = new ServiceProjectResource(_httpClient, ProjectName);
    }

    /// <summary>
    ///     Gets current project name to interact with.
    /// </summary>
    public string ProjectName { get; }

    /// <inheritdoc cref="ILaunchResource" />
    public ILaunchResource Launch { get; }

    /// <inheritdoc cref="IAsyncLaunchResource" />
    public IAsyncLaunchResource AsyncLaunch { get; }

    /// <inheritdoc cref="ITestItemResource" />
    public ITestItemResource TestItem { get; }

    /// <inheritdoc cref="IAsyncTestItemResource" />
    public IAsyncTestItemResource AsyncTestItem { get; }

    /// <inheritdoc cref="ILogItemResource" />
    public ILogItemResource LogItem { get; }

    /// <inheritdoc cref="IAsyncLogItemResource" />
    public IAsyncLogItemResource AsyncLogItem { get; }

    /// <inheritdoc cref="IUserResource" />
    public IUserResource User { get; }

    /// <inheritdoc cref="IUserFilterResource" />
    public IUserFilterResource UserFilter { get; }

    /// <inheritdoc cref="IProjectResource" />
    public IProjectResource Project { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        if (args.Name.StartsWith("System.Text.Json", StringComparison.OrdinalIgnoreCase))
        {
            return Assembly.Load("System.Text.Json");
        }

        if (args.Name.StartsWith("System.Text.Encodings.Web", StringComparison.OrdinalIgnoreCase))
        {
            return Assembly.Load("System.Text.Encodings.Web");
        }

        return null;
    }
}
