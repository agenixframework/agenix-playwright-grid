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

using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using Npgsql;

namespace Agenix.PlaywrightGrid.Integration.Tests.Builders;

/// <summary>
///     Fluent builder for creating launch test data.
///     Provides a chainable API for setting launch properties and creating launches in the database.
/// </summary>
public class LaunchBuilder
{
    private Guid _launchId = Guid.NewGuid();
    private int _launchNumber = 1;
    private string _ownerApiKey = TestConstants.DefaultOwnerApiKey;
    private string _projectKey = TestConstants.DefaultProjectKey;
    private string _status = TestConstants.LaunchStatus.InProgress;

    /// <summary>
    ///     Sets the launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID.</param>
    /// <returns>This builder for chaining.</returns>
    public LaunchBuilder WithId(Guid launchId)
    {
        _launchId = launchId;
        return this;
    }

    /// <summary>
    ///     Sets the project key.
    /// </summary>
    /// <param name="projectKey">The project key.</param>
    /// <returns>This builder for chaining.</returns>
    public LaunchBuilder WithProjectKey(string projectKey)
    {
        _projectKey = projectKey;
        return this;
    }

    /// <summary>
    ///     Sets the launch number.
    /// </summary>
    /// <param name="launchNumber">The launch number.</param>
    /// <returns>This builder for chaining.</returns>
    public LaunchBuilder WithLaunchNumber(int launchNumber)
    {
        _launchNumber = launchNumber;
        return this;
    }

    /// <summary>
    ///     Sets the launch status.
    /// </summary>
    /// <param name="status">The launch status (e.g., InProgress, Finished, Failed).</param>
    /// <returns>This builder for chaining.</returns>
    public LaunchBuilder WithStatus(string status)
    {
        _status = status;
        return this;
    }

    /// <summary>
    ///     Sets the owner API key.
    /// </summary>
    /// <param name="ownerApiKey">The API key of the user who created the launch.</param>
    /// <returns>This builder for chaining.</returns>
    public LaunchBuilder WithOwnerApiKey(string ownerApiKey)
    {
        _ownerApiKey = ownerApiKey;
        return this;
    }

    /// <summary>
    ///     Creates the launch in the database using the configured parameters.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The launch ID of the created launch.</returns>
    public async Task<Guid> CreateAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await DatabaseHelpers.CreateLaunchAsync(
            dataSource,
            _launchId,
            _projectKey,
            _launchNumber,
            _status,
            _ownerApiKey,
            cancellationToken);

        return _launchId;
    }

    /// <summary>
    ///     Creates the launch in the database using the singleton PostgresTestFixture.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The launch ID of the created launch.</returns>
    public Task<Guid> CreateAsync(CancellationToken cancellationToken = default)
    {
        return CreateAsync(PostgresTestFixture.Instance.DataSource, cancellationToken);
    }
}
