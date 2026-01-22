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
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Fixtures;

/// <summary>
///     Base class for database-focused integration tests.
///     Provides shared database connection, cleanup, and helper methods.
/// </summary>
[TestFixture]
public abstract class DatabaseTestBase
{
    /// <summary>
    ///     One-time setup for the test fixture.
    ///     Logs the database connection string and performs initial cleanup.
    /// </summary>
    [OneTimeSetUp]
    public virtual async Task OneTimeSetup()
    {
        await TestContext.Progress.WriteLineAsync(
            $"[{GetType().Name}] Using PostgreSQL at {PostgresTestFixture.Instance.ConnectionString}");

        await CleanupTestData();
    }

    /// <summary>
    ///     One-time teardown for the test fixture.
    ///     Performs final cleanup.
    /// </summary>
    [OneTimeTearDown]
    public virtual async Task OneTimeTearDown()
    {
        await CleanupTestData();
    }

    /// <summary>
    ///     Setup before each test.
    ///     Ensures a clean database state.
    /// </summary>
    [SetUp]
    public virtual async Task Setup()
    {
        await CleanupTestData();
    }

    /// <summary>
    ///     Teardown after each test.
    ///     Ensures cleanup of test data.
    /// </summary>
    [TearDown]
    public virtual async Task TearDown()
    {
        await CleanupTestData();
    }

    /// <summary>
    ///     Gets the shared database data source for test execution.
    /// </summary>
    protected NpgsqlDataSource Db => PostgresTestFixture.Instance.DataSource;

    /// <summary>
    ///     Gets the project key used for this test class.
    ///     Override this property to use a different project key for isolation.
    /// </summary>
    protected virtual string ProjectKey => TestConstants.DefaultProjectKey;

    /// <summary>
    ///     Cleans up all test data for the project key.
    ///     Override this method to customize cleanup behavior.
    /// </summary>
    protected virtual async Task CleanupTestData()
    {
        await DatabaseHelpers.CleanupProjectDataAsync(Db, ProjectKey);
    }
}
