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
using Agenix.PlaywrightGrid.Shared.Logging;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace PlaywrightHub.Tests.Infrastructure.Web;

/// <summary>
/// Tests for <see cref="EventCodes.AdminProjectsUsers"/> to ensure event codes are properly defined
/// and follow project conventions for structured logging and monitoring.
/// </summary>
[TestFixture]
public partial class AdminProjectsUsersEventCodesTests
{
    /// <summary>
    /// Validates that all event codes across all categories are unique.
    /// This is critical to prevent duplicate event codes which would break logging and monitoring.
    /// </summary>
    [Test]
    public void AllEventCodes_ShouldBeUnique()
    {
        // Arrange & Act
        var allCodes = GetAllEventCodes();
        var uniqueCodes = new HashSet<string>(allCodes);

        // Assert
        Assert.That(allCodes, Is.Unique, "Event codes must be unique across all categories");
        Assert.That(uniqueCodes.Count, Is.EqualTo(allCodes.Count()), "All event codes must be unique");
    }

    /// <summary>
    /// Validates that all event codes follow the ADM## format (ADM + exactly 2 digits).
    /// This ensures consistency with the established naming convention for admin events.
    /// </summary>
    [Test]
    public void AllEventCodes_ShouldFollowAdmFormat()
    {
        // Arrange
        var allCodes = GetAllEventCodes();
        var codePattern = MyRegex();

        // Act & Assert
        foreach (var code in allCodes)
        {
            Assert.That(codePattern.IsMatch(code), Is.True,
                $"Event code '{code}' must follow ADM## format (ADM + 2 digits)");
        }
    }

    /// <summary>
    /// Validates that all event codes are non-null and non-empty strings.
    /// This catches any accidentally undefined or null event code constants.
    /// </summary>
    [Test]
    public void AllEventCodes_ShouldBeNonEmpty()
    {
        // Arrange
        var allCodes = GetAllEventCodes();

        // Act & Assert
        foreach (var code in allCodes)
        {
            Assert.That(code, Is.Not.Null, "Event code cannot be null");
            Assert.That(code, Is.Not.Empty, "Event code cannot be empty string");
            Assert.That(code.Length, Is.EqualTo(5), "Event code must be exactly 5 characters (ADM + 2 digits)");
        }
    }

    /// <summary>
    /// Validates that no event codes are duplicated within a single category.
    /// This catches accidental duplicate definitions within the same nested class.
    /// </summary>
    [Test]
    public void Categories_ShouldNotContainDuplicateCodes()
    {
        // Arrange
        var categories = typeof(EventCodes.AdminProjectsUsers).GetNestedTypes();

        // Act & Assert
        foreach (var category in categories)
        {
            var fields = category.GetFields(BindingFlags.Public | BindingFlags.Static);
            var codes = fields.Select(f => f.GetValue(null) as string).OfType<string>().ToList();

            Assert.That(codes, Is.Unique, $"Category {category.Name} contains duplicate event codes");
        }
    }

    /// <summary>
    /// Validates that the event code categories are properly structured with expected nested classes.
    /// </summary>
    [Test]
    public void EventCodes_ShouldHaveExpectedCategories()
    {
        // Arrange & Act
        var categories = typeof(EventCodes.AdminProjectsUsers).GetNestedTypes();
        var categoryNames = categories.Select(c => c.Name).OrderBy(n => n).ToList();

        // Assert
        Assert.That(categoryNames, Has.Member("Authentication"), "Should have Authentication category");
        Assert.That(categoryNames, Has.Member("UserManagement"), "Should have UserManagement category");
        Assert.That(categoryNames, Has.Member("ProjectManagement"), "Should have ProjectManagement category");
        Assert.That(categoryNames, Has.Member("MembershipManagement"), "Should have MembershipManagement category");
        Assert.That(categoryNames, Has.Member("AdminUserManagement"), "Should have AdminUserManagement category");
        Assert.That(categoryNames, Has.Member("Validation"), "Should have Validation category");
        Assert.That(categoryNames, Has.Member("RateLimiting"), "Should have RateLimiting category");
        Assert.That(categoryNames, Has.Member("Query"), "Should have Query category");
        Assert.That(categories.Length, Is.EqualTo(8), "Should have exactly 8 event code categories");
    }

    /// <summary>
    /// Validates that each category contains expected event codes.
    /// This acts as a regression test to ensure codes aren't accidentally removed.
    /// </summary>
    [Test]
    public void AuthenticationCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("Authentication");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM01"), "Should have LoginAttempt (ADM01)");
        Assert.That(codeValues, Has.Member("ADM02"), "Should have LoginSucceeded (ADM02)");
        Assert.That(codeValues, Has.Member("ADM03"), "Should have LoginFailed (ADM03)");
        Assert.That(codeValues, Has.Member("ADM04"), "Should have LoginRateLimitExceeded (ADM04)");
        Assert.That(codeValues, Has.Member("ADM05"), "Should have Logout (ADM05)");
        Assert.That(codeValues.Count, Is.EqualTo(5), "Authentication should have 5 event codes");
    }

    [Test]
    public void UserManagementCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("UserManagement");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM21"), "Should have UserCreated (ADM21)");
        Assert.That(codeValues, Has.Member("ADM22"), "Should have UserUpdated (ADM22)");
        Assert.That(codeValues, Has.Member("ADM23"), "Should have UserDeleted (ADM23)");
        Assert.That(codeValues, Has.Member("ADM24"), "Should have UserActivated (ADM24)");
        Assert.That(codeValues, Has.Member("ADM25"), "Should have UserDeactivated (ADM25)");
        Assert.That(codeValues, Has.Member("ADM26"), "Should have UserPasswordReset (ADM26)");
        Assert.That(codeValues.Count, Is.EqualTo(6), "UserManagement should have 6 event codes");
    }

    [Test]
    public void ProjectManagementCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("ProjectManagement");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM31"), "Should have ProjectCreated (ADM31)");
        Assert.That(codeValues, Has.Member("ADM32"), "Should have ProjectUpdated (ADM32)");
        Assert.That(codeValues, Has.Member("ADM33"), "Should have ProjectDeleted (ADM33)");
        Assert.That(codeValues, Has.Member("ADM34"), "Should have ProjectArchived (ADM34)");
        Assert.That(codeValues, Has.Member("ADM35"), "Should have ProjectRestored (ADM35)");
        Assert.That(codeValues.Count, Is.EqualTo(5), "ProjectManagement should have 5 event codes");
    }

    [Test]
    public void MembershipManagementCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("MembershipManagement");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM41"), "Should have MembershipAdded (ADM41)");
        Assert.That(codeValues, Has.Member("ADM42"), "Should have MembershipRemoved (ADM42)");
        Assert.That(codeValues, Has.Member("ADM43"), "Should have MembershipRoleUpdated (ADM43)");
        Assert.That(codeValues.Count, Is.EqualTo(3), "MembershipManagement should have 3 event codes");
    }

    [Test]
    public void AdminUserManagementCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("AdminUserManagement");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM51"), "Should have AdminInvited (ADM51)");
        Assert.That(codeValues, Has.Member("ADM52"), "Should have InviteAccepted (ADM52)");
        Assert.That(codeValues, Has.Member("ADM53"), "Should have InviteExpired (ADM53)");
        Assert.That(codeValues, Has.Member("ADM54"), "Should have InviteRevoked (ADM54)");
        Assert.That(codeValues.Count, Is.EqualTo(4), "AdminUserManagement should have 4 event codes");
    }

    [Test]
    public void ValidationCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("Validation");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM91"), "Should have ValidationFailed (ADM91)");
        Assert.That(codeValues, Has.Member("ADM92"), "Should have ValidationSucceeded (ADM92)");
        Assert.That(codeValues.Count, Is.EqualTo(2), "Validation should have 2 event codes");
    }

    [Test]
    public void RateLimitingCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("RateLimiting");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM95"), "Should have RateLimitExceeded (ADM95)");
        Assert.That(codeValues, Has.Member("ADM96"), "Should have ApiRateLimitExceeded (ADM96)");
        Assert.That(codeValues, Has.Member("ADM97"), "Should have ProjectCreationRateLimitExceeded (ADM97)");
        Assert.That(codeValues.Count, Is.EqualTo(3), "RateLimiting should have 3 event codes");
    }

    [Test]
    public void QueryCategory_ShouldHaveExpectedCodes()
    {
        // Arrange & Act
        var codes = GetCategoryCodes("Query");
        var codeValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

        // Assert
        Assert.That(codeValues, Has.Member("ADM11"), "Should have ProjectListRetrieved (ADM11)");
        Assert.That(codeValues, Has.Member("ADM12"), "Should have ProjectDetailsRetrieved (ADM12)");
        Assert.That(codeValues, Has.Member("ADM13"), "Should have UserListRetrieved (ADM13)");
        Assert.That(codeValues, Has.Member("ADM14"), "Should have UserDetailsRetrieved (ADM14)");
        Assert.That(codeValues, Has.Member("ADM15"), "Should have MembershipListRetrieved (ADM15)");
        Assert.That(codeValues.Count, Is.EqualTo(5), "Query should have 5 event codes");
    }

    /// <summary>
    /// Validates that event codes are in proper sequential order within each category.
    /// This helps catch gaps in numbering that might indicate missing codes.
    /// </summary>
    [Test]
    public void EventCodes_ShouldHaveSequentialNumbersWithinCategories()
    {
        // Arrange & Act
        var categories = typeof(EventCodes.AdminProjectsUsers).GetNestedTypes();

        foreach (var category in categories)
        {
            var codes = GetCategoryCodes(category.Name);
            var sortedValues = codes.Select(c => c.GetValue(null) as string).OfType<string>().OrderBy(v => v).ToList();

            // Extract numeric parts (ADM##)
            var numbers = sortedValues.Select(c => int.Parse(c[3..])).ToList();

            // Check for gaps (sequential numbers)
            for (var i = 1; i < numbers.Count; i++)
            {
                if (numbers[i] - numbers[i - 1] != 1)
                {
                    Assert.Fail($"Category {category.Name} has gap in sequential numbering between {numbers[i - 1]} and {numbers[i]}");
                }
            }
        }
    }

    // Helper Methods

    private static IEnumerable<string> GetAllEventCodes()
    {
        return typeof(EventCodes.AdminProjectsUsers)
            .GetNestedTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(f => f.GetValue(null) as string)
            .OfType<string>();
    }

    private static List<FieldInfo> GetCategoryCodes(string categoryName)
    {
        var categoryType = typeof(EventCodes.AdminProjectsUsers).GetNestedType(categoryName);
        Assert.That(categoryType, Is.Not.Null, $"Category {categoryName} not found");

        return categoryType!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToList();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^ADM\d{2}$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
