# SDD Stage 3: Task Breakdown - Project Rename

## Tasks: Rename to "Agenix Test Platform"

### Task Dependency Graph

```
[Task 1: Backup & Branch Creation]
    ↓
[Task 2: Rename Solution File]
    ↓
[Task 3: Rename Project Files & Folders] ← [Build Verification]
    ↓
[Task 4: Update C# Namespaces] ← [Build Verification]
    ↓
[Task 5: Update Using Statements] ← [Build Verification]
    ↓
[Task 6: Update Assembly Names] ← [Build Verification]
    ↓
[Task 7: Update Documentation] ← [Task 8: Update Configuration Comments]
    ↓
[Task 9: Final Build & Test Verification]
    ↓
[Task 10: Git Commit & Cleanup]
```

### Task List

---

#### Task 1: Backup & Branch Creation
- **Complexity**: Low
- **Estimated Time**: 5 minutes
- **Files to Create/Modify**: None (Git operations only)
- **Dependencies**: None
- **Implementation Steps**:
  1. Create backup branch: `git checkout -b backup/pre-rename-$(date +%Y%m%d)`
  2. Create feature branch: `git checkout -b feature/rename-to-test-platform`
  3. Verify clean working directory: `git status`
- **Verification**:
  - [ ] Feature branch exists
  - [ ] No uncommitted changes
  - [ ] Backup branch created

---

#### Task 2: Rename Solution File
- **Complexity**: Low
- **Estimated Time**: 5 minutes
- **Files to Create/Modify**:
  - `PlaywrightGrid.sln` → `AgenixTestPlatform.sln` (rename)
  - `PlaywrightGrid.sln.DotSettings.user` → `AgenixTestPlatform.sln.DotSettings.user` (rename)
- **Dependencies**: Task 1
- **Implementation Steps**:
  1. Rename solution file: `git mv PlaywrightGrid.sln AgenixTestPlatform.sln`
  2. Rename user settings: `git mv PlaywrightGrid.sln.DotSettings.user AgenixTestPlatform.sln.DotSettings.user`
  3. Open solution in Rider to verify it loads
- **Verification**:
  - [ ] Solution file renamed
  - [ ] Solution opens in Rider without errors
  - [ ] All projects visible in solution explorer

---

#### Task 3: Rename Project Files & Folders
- **Complexity**: Medium
- **Estimated Time**: 30 minutes
- **Files to Create/Modify**:
  - `Agenix.PlaywrightGrid.Client/` → `Agenix.TestPlatform.Client/` (rename folder)
  - `Agenix.PlaywrightGrid.Client/Agenix.PlaywrightGrid.Client.csproj` → `Agenix.TestPlatform.Client/Agenix.TestPlatform.Client.csproj` (rename)
  - `Agenix.PlaywrightGrid.Domain/` → `Agenix.TestPlatform.Domain/` (rename folder)
  - `Agenix.PlaywrightGrid.Domain/Agenix.PlaywrightGrid.Domain.csproj` → `Agenix.TestPlatform.Domain/Agenix.TestPlatform.Domain.csproj` (rename)
  - `Agenix.PlaywrightGrid.Shared/` → `Agenix.TestPlatform.Shared/` (rename folder)
  - `Agenix.PlaywrightGrid.Shared/Agenix.PlaywrightGrid.Shared.csproj` → `Agenix.TestPlatform.Shared/Agenix.TestPlatform.Shared.csproj` (rename)
  - `Agenix.PlaywrightGrid.Integration.Tests/` → `Agenix.TestPlatform.Integration.Tests/` (rename folder)
  - `Agenix.PlaywrightGrid.Integration.Tests/Agenix.PlaywrightGrid.Integration.Tests.csproj` → `Agenix.TestPlatform.Integration.Tests/Agenix.TestPlatform.Integration.Tests.csproj` (rename)
  - `Agenix.PlaywrightGrid.Domain.Tests/` → `Agenix.TestPlatform.Domain.Tests/` (rename folder)
  - `Agenix.PlaywrightGrid.Domain.Tests/Agenix.PlaywrightGrid.Domain.Tests.csproj` → `Agenix.TestPlatform.Domain.Tests/Agenix.TestPlatform.Domain.Tests.csproj` (rename)
  - `Agenix.PlaywrightGrid.Shared.Tests/` → `Agenix.TestPlatform.Shared.Tests/` (rename folder)
  - `Agenix.PlaywrightGrid.Shared.Tests/Agenix.PlaywrightGrid.Shared.Tests.csproj` → `Agenix.TestPlatform.Shared.Tests/Agenix.TestPlatform.Shared.Tests.csproj` (rename)
  - `PlaywrightHub.Tests/` → `Agenix.TestPlatform.Hub.Tests/` (rename folder)
  - `PlaywrightHub.Tests/PlaywrightHub.Tests.csproj` → `Agenix.TestPlatform.Hub.Tests/Agenix.TestPlatform.Hub.Tests.csproj` (rename)
  - `WorkerService.Tests/` → `Agenix.TestPlatform.Worker.Tests/` (rename folder)
  - `WorkerService.Tests/WorkerService.Tests.csproj` → `Agenix.TestPlatform.Worker.Tests/Agenix.TestPlatform.Worker.Tests.csproj` (rename)
  - `Dashboard.Tests/` → `Agenix.TestPlatform.Dashboard.Tests/` (rename folder)
  - `Dashboard.Tests/Dashboard.Tests.csproj` → `Agenix.TestPlatform.Dashboard.Tests/Agenix.TestPlatform.Dashboard.Tests.csproj` (rename)
  - `AgenixTestPlatform.sln` (update project references)
- **Dependencies**: Task 2
- **Implementation Steps**:
  1. Use `git mv` to rename each project folder
  2. Rename `.csproj` file inside each folder
  3. Update solution file project references
  4. Update `<ProjectReference>` in dependent `.csproj` files
  5. Reload solution in Rider
- **Verification**:
  - [ ] All project folders renamed
  - [ ] All `.csproj` files renamed
  - [ ] Solution loads all projects
  - [ ] `dotnet build` succeeds (may have namespace errors, that's OK)

---

#### Task 4: Update C# Namespaces
- **Complexity**: Medium
- **Estimated Time**: 45 minutes
- **Files to Create/Modify**:
  - All `.cs` files in `Agenix.TestPlatform.Client/`
  - All `.cs` files in `Agenix.TestPlatform.Domain/`
  - All `.cs` files in `Agenix.TestPlatform.Shared/`
  - All `.cs` files in `Agenix.TestPlatform.Integration.Tests/`
  - All `.cs` files in `Agenix.TestPlatform.Domain.Tests/`
  - All `.cs` files in `Agenix.TestPlatform.Shared.Tests/`
  - All `.cs` files in `Agenix.TestPlatform.Hub.Tests/`
  - All `.cs` files in `Agenix.TestPlatform.Worker.Tests/`
  - All `.cs` files in `Agenix.TestPlatform.Dashboard.Tests/`
  - All `.cs` files in `hub/` (namespace declarations)
  - All `.cs` files in `worker/` (namespace declarations)
  - All `.cs` files in `dashboard/` (namespace declarations)
  - All `.cs` files in `ingestion/` (namespace declarations)
  - All `.cs` files in `housekeeping-service/` (namespace declarations)
- **Dependencies**: Task 3
- **Implementation Steps**:
  1. Use Rider "Find in Files" to locate all `namespace Agenix.PlaywrightGrid.*` declarations
  2. Replace with `namespace Agenix.TestPlatform.*`
  3. Use Rider "Find in Files" to locate all `namespace PlaywrightHub.*` declarations
  4. Replace with `namespace Agenix.TestPlatform.Hub.*`
  5. Build to verify namespace changes
- **Verification**:
  - [ ] All namespace declarations updated
  - [ ] No `Agenix.PlaywrightGrid` references in namespace declarations
  - [ ] No `PlaywrightHub` references in namespace declarations
  - [ ] `dotnet build` succeeds (may have using statement errors, that's OK)

---

#### Task 5: Update Using Statements
- **Complexity**: Medium
- **Estimated Time**: 30 minutes
- **Files to Create/Modify**:
  - All `.cs` files with `using Agenix.PlaywrightGrid.*`
  - All `.cs` files with `using PlaywrightHub.*`
  - All `.razor` files with `@using Agenix.PlaywrightGrid.*`
  - All `.razor` files with `@using PlaywrightHub.*`
- **Dependencies**: Task 4
- **Implementation Steps**:
  1. Use Rider "Find in Files" to locate all `using Agenix.PlaywrightGrid` statements
  2. Replace with `using Agenix.TestPlatform`
  3. Use Rider "Find in Files" to locate all `using PlaywrightHub` statements
  4. Replace with `using Agenix.TestPlatform.Hub`
  5. Use Rider "Find in Files" to locate all `@using Agenix.PlaywrightGrid` statements
  6. Replace with `@using Agenix.TestPlatform`
  7. Build to verify using statement changes
- **Verification**:
  - [ ] All using statements updated
  - [ ] No `using Agenix.PlaywrightGrid` references
  - [ ] No `using PlaywrightHub` references
  - [ ] `dotnet build` succeeds (may have assembly name errors, that's OK)

---

#### Task 6: Update Assembly Names
- **Complexity**: Low
- **Estimated Time**: 15 minutes
- **Files to Create/Modify**:
  - All `.csproj` files with `<AssemblyName>` tags
  - All `.csproj` files with `<RootNamespace>` tags
- **Dependencies**: Task 5
- **Implementation Steps**:
  1. Open each `.csproj` file
  2. Update `<AssemblyName>Agenix.PlaywrightGrid.*</AssemblyName>` → `<AssemblyName>Agenix.TestPlatform.*</AssemblyName>`
  3. Update `<RootNamespace>Agenix.PlaywrightGrid.*</RootNamespace>` → `<RootNamespace>Agenix.TestPlatform.*</RootNamespace>`
  4. Update `<RootNamespace>PlaywrightHub.*</RootNamespace>` → `<RootNamespace>Agenix.TestPlatform.Hub.*</RootNamespace>`
  5. Build to verify
- **Verification**:
  - [ ] All assembly names updated
  - [ ] All root namespaces updated
  - [ ] `dotnet build` succeeds with 0 errors

---

#### Task 7: Update Documentation
- **Complexity**: Medium
- **Estimated Time**: 30 minutes
- **Files to Create/Modify**:
  - `CLAUDE.md` (all references)
  - `README.md` (all references)
  - `docs/**/*.md` (all documentation files)
- **Dependencies**: Task 6
- **Implementation Steps**:
  1. Use global find & replace in `CLAUDE.md`:
     - "Agenix Playwright Grid" → "Agenix Test Platform"
     - "Agenix Playwright Service" → "Agenix Test Platform"
     - "PlaywrightGrid" → "TestPlatform" (in code examples)
  2. Update `README.md` title and description
  3. Update all files in `docs/` folder
  4. Verify markdown formatting with preview
- **Verification**:
  - [ ] `CLAUDE.md` updated (no old references)
  - [ ] `README.md` updated
  - [ ] All `docs/*.md` files updated
  - [ ] Markdown formatting valid

---

#### Task 8: Update Configuration Comments
- **Complexity**: Low
- **Estimated Time**: 15 minutes
- **Files to Create/Modify**:
  - `.env` (comments only)
  - `docker-compose.yml` (comments only)
  - `docker-compose.workers.yml` (comments only)
  - `Dockerfile` files (comments only)
  - Shell scripts in `scripts/` (comments only)
- **Dependencies**: Task 6
- **Implementation Steps**:
  1. Update comments in `.env` file
  2. Update comments in `docker-compose.yml`
  3. Update comments in `docker-compose.workers.yml`
  4. Update comments in Dockerfiles (hub, worker, dashboard, ingestion, housekeeping)
  5. Update comments in shell scripts
- **Verification**:
  - [ ] All configuration file comments updated
  - [ ] No changes to variable names or service names
  - [ ] Docker Compose validates: `docker-compose config`

---

#### Task 9: Final Build & Test Verification
- **Complexity**: Medium
- **Estimated Time**: 30 minutes
- **Files to Create/Modify**: None (verification only)
- **Dependencies**: Task 7, Task 8
- **Implementation Steps**:
  1. Clean solution: `dotnet clean`
  2. Build solution: `dotnet build`
  3. Run all unit tests: `dotnet test`
  4. Start services: `./scripts/run-local-dev-inline.sh`
  5. Run integration tests
  6. Manual smoke test (open dashboard, create launch)
  7. Stop services
- **Verification**:
  - [ ] `dotnet build` succeeds (0 errors)
  - [ ] All unit tests pass
  - [ ] All integration tests pass
  - [ ] Dashboard loads correctly
  - [ ] Can create and view launches

---

#### Task 10: Git Commit & Cleanup
- **Complexity**: Low
- **Estimated Time**: 10 minutes
- **Files to Create/Modify**: None (Git operations only)
- **Dependencies**: Task 9
- **Implementation Steps**:
  1. Stage all changes: `git add -A`
  2. Commit with message:
     ```
     Refactor: Rename project to "Agenix Test Platform"

     - Renamed solution file: PlaywrightGrid.sln → AgenixTestPlatform.sln
     - Renamed all project files and folders
     - Updated C# namespaces: Agenix.PlaywrightGrid.* → Agenix.TestPlatform.*
     - Updated PlaywrightHub.* → Agenix.TestPlatform.Hub.*
     - Updated all documentation (CLAUDE.md, README.md, docs/)
     - Updated configuration file comments
     - Verified: Build succeeds, all tests pass

     Breaking Changes: None (API endpoints, environment variables, database schema unchanged)

     Migration: Update using statements in client code
     ```
  3. Optional: Delete backup branch
  4. Push to remote (if applicable)
- **Verification**:
  - [ ] All changes committed
  - [ ] Commit message follows convention
  - [ ] Feature branch ready for merge

---

### Execution Strategy

**Phase 1: Structure Changes (Tasks 1-3)** - 40 minutes
- Set up branch and rename files/folders
- Focus: File system and solution structure
- Expected: Build may fail (namespace errors), that's OK

**Phase 2: Code Changes (Tasks 4-6)** - 90 minutes
- Update namespaces, using statements, assembly names
- Focus: C# code correctness
- Expected: Build succeeds by end of phase

**Phase 3: Documentation (Tasks 7-8)** - 45 minutes
- Update all documentation and configuration comments
- Focus: Consistency and completeness
- Expected: No build changes

**Phase 4: Verification (Tasks 9-10)** - 40 minutes
- Full build, test, and smoke test
- Git commit and cleanup
- Focus: Quality assurance

**Total Estimated Time**: 3.5 hours (conservative estimate)

### Rollback Plan

If issues arise during implementation:

- **After Task 1-2**: Delete feature branch, return to backup branch
- **After Task 3-6**: Use `git reset --hard` to previous commit, investigate build errors
- **After Task 7-9**: Code is functional, only documentation affected - safe to continue

### Risk Mitigation

1. **Build Failures**: Commit after each phase for incremental rollback points
2. **Missed References**: Use global search after completion to verify
3. **Test Failures**: Investigate immediately, don't proceed to next task
4. **Deployment Issues**: Configuration backward compatible (no deployment changes needed)

---

**Date**: 2025-12-27
**Status**: Ready for Implementation
