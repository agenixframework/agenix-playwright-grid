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

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class AdminProjectsUsersDomainTests
{
    [Test]
    public void Validate_Project_Key_And_Name()
    {
        Assert.That(AdminValidation.TryValidateProjectKey("proj-1", out var e1), Is.True);
        Assert.That(e1, Is.Null);
        Assert.That(AdminValidation.TryValidateProjectName("Project One", out var e2), Is.True);
        Assert.That(e2, Is.Null);

        Assert.That(AdminValidation.TryValidateProjectKey("bad key", out var e3), Is.False);
        Assert.That(e3, Does.Contain("key allows"));
    }

    [Test]
    public void Validate_User_Id_Username_Email()
    {
        Assert.That(AdminValidation.TryValidateUserId("user_1", out var e1), Is.True);
        Assert.That(e1, Is.Null);
        Assert.That(AdminValidation.TryValidateUsername("Jane Doe", out var e2), Is.True);
        Assert.That(e2, Is.Null);
        Assert.That(AdminValidation.TryValidateEmail("jane.doe@example.com", out var e3), Is.True);
        Assert.That(e3, Is.Null);

        Assert.That(AdminValidation.TryValidateUsername("Bad@Name", out var e4), Is.False);
        Assert.That(e4, Does.Contain("username allows"));
    }

    [Test]
    public void Membership_Validation_And_Defaults()
    {
        Assert.That(AdminValidation.TryValidateMembership("u1", "p1", out var e), Is.True);
        Assert.That(e, Is.Null);

        var m = new Membership
        {
            UserId = "u1",
            ProjectKey = "p1",
            Role = ProjectRole.Member,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        Assert.That(m.Role, Is.EqualTo(ProjectRole.Member));
    }

    [Test]
    public void Uniqueness_Helpers_Work()
    {
        var projects = new List<Project>
        {
            new() { Key = "p1", Name = "Project One", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow }
        };
        var users = new List<User>
        {
            new() { Id = "u1", Username = "User One", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow }
        };

        Assert.That(AdminValidation.IsUniqueProjectKey(projects, "p2", out var pe1), Is.True);
        Assert.That(pe1, Is.Null);
        Assert.That(AdminValidation.IsUniqueProjectKey(projects, "P1", out var pe2), Is.False); // case-insensitive
        Assert.That(pe2, Does.Contain("already exists"));

        Assert.That(AdminValidation.IsUniqueProjectName(projects, "Project Two", out var pne1), Is.True);
        Assert.That(AdminValidation.IsUniqueUserId(users, "u2", out var ue1), Is.True);
        Assert.That(AdminValidation.IsUniqueUserEmail(users, "a@b.c", out var ue2), Is.True);
    }
}
