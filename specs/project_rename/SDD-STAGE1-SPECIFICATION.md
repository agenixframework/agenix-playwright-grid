# SDD Stage 1: Project Rename Specification

## Feature: Rename Project from "Agenix Playwright Grid" to "Agenix Test Platform"

### Overview
Rename all occurrences of "Agenix Playwright Service" and "Agenix Playwright Grid" to "Agenix Test Platform" across the entire codebase, including solution files, project files, namespaces, documentation, and configuration files.

### User Stories

**As a** developer
**I want to** have consistent project naming throughout the codebase
**So that** the project identity is clear and unified

**As a** user reading documentation
**I want to** see consistent product naming
**So that** I understand what product I'm using without confusion

**As a** system administrator
**I want to** see consistent naming in logs and configuration
**So that** I can easily identify and manage the platform

### Acceptance Criteria

- [ ] All references to "Agenix Playwright Service" replaced with "Agenix Test Platform"
- [ ] All references to "Agenix Playwright Grid" "Agenix PlaywrightGrid" replaced with "Agenix Test Platform"
- [ ] Solution file renamed: `PlaywrightGrid.sln` → `AgenixTestPlatform.sln`
- [ ] All `.csproj` files renamed from `*.PlaywrightGrid.*` to `*.TestPlatform.*`
- [ ] All C# namespaces updated from `Agenix.PlaywrightGrid.*` to `Agenix.TestPlatform.*`
- [ ] All folder names updated to reflect new naming
- [ ] All documentation files (CLAUDE.md, README.md, docs/*) updated
- [ ] All configuration files (.env, docker-compose.yml) updated
- [ ] All shell scripts updated
- [ ] Git repository directory can be renamed (optional, user decision)
- [ ] Build succeeds with 0 errors after rename
- [ ] All tests pass after rename

### Constraints

- **Technical**: Must maintain backward compatibility for existing deployments (API endpoints, database tables)
- **Performance**: Rename should not affect runtime performance
- **Compatibility**: Existing Docker images and deployments should continue to work
- **Timeline**: Single session refactoring (2-3 hours estimated)

### Out of Scope

- Renaming database tables (e.g., `launches`, `test_items`) - these remain unchanged for backward compatibility
- Renaming environment variables (e.g., `AGENIX_HUB_*`) - these remain unchanged for compatibility
- Renaming Docker image names - these remain unchanged
- Renaming API endpoints - these remain unchanged
- Renaming existing git history/commits - these remain unchanged

### Success Metrics

- 100% of project/solution files renamed
- 100% of C# namespaces updated
- 100% of documentation updated
- 0 build errors after rename
- All integration tests pass

### Rationale

**Why rename from "Playwright Grid" to "Test Platform"?**

1. **Broader Scope**: The platform is not limited to Playwright; it's a general test execution and reporting platform
2. **Marketing**: "Test Platform" is more descriptive and professional
3. **Future-Proofing**: Allows expansion beyond Playwright (Selenium, Cypress, etc.)
4. **Clarity**: "Grid" implies only browser management; "Platform" encompasses the full feature set (launches, test items, reporting, artifacts, etc.)

### Migration Path for Existing Deployments

**For users upgrading from older versions**:

1. **Code Changes**: Update using statements in client code
   ```csharp
   // OLD
   using Agenix.PlaywrightGrid.Client;

   // NEW
   using Agenix.TestPlatform.Client;
   ```

2. **NuGet Packages**: New package names (future)
   - `Agenix.PlaywrightGrid.Client` → `Agenix.TestPlatform.Client`

3. **No Breaking Changes**: API endpoints, database schema, environment variables remain unchanged

### Risk Assessment

**Low Risk**:
- Pure naming refactor, no logic changes
- Build system will catch any missed references
- Tests will validate functionality remains intact

**Mitigation**:
- Create backup branch before starting
- Run full test suite after each phase
- Use IDE refactoring tools where possible

### Stakeholder Sign-Off

- [X] User approves rename from "Agenix Playwright Grid" to "Agenix Test Platform"
- [X] User approves solution file rename to `AgenixTestPlatform.sln`
- [X] User approves namespace changes to `Agenix.TestPlatform.*`
- [X] User confirms backward compatibility requirements

---

**Date**: 2025-12-27
**Status**: Approved
