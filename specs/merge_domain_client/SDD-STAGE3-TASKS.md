# SDD Stage 3: Task Breakdown - Merge Domain into Client

## Tasks: Merge Domain into Client Package

### Task Dependency Graph

```
[Task 1: Backup & Branch Creation]
    ↓
[Task 2: Move Domain Files to Client/Domain/]
    ↓
[Task 3: Update Namespaces in Moved Files] ← [Build Client Project]
    ↓
[Task 4: Update Project References]
    ↓
[Task 5: Update Using Statements] ← [Build All Projects]
    ↓
[Task 6: Delete Domain Project]
    ↓
[Task 7: Rename Domain.Tests Project]
    ↓
[Task 8: Final Build & Test Verification]
    ↓
[Task 9: Git Commit & Cleanup]
```

### Task List

---

#### Task 1: Backup & Branch Creation
- **Complexity**: Low
- **Estimated Time**: 5 minutes
- **Files to Create/Modify**: None (Git operations only)
- **Dependencies**: None
- **Implementation Steps**:
  1. Create backup branch: `git checkout -b backup/pre-domain-merge-$(date +%Y%m%d)`
  2. Create feature branch: `git checkout -b feature/merge-domain-into-client`
  3. Verify clean working directory: `git status`
- **Verification**:
  - [ ] Feature branch exists
  - [ ] No uncommitted changes
  - [ ] Backup branch created

---

#### Task 2: Move Domain Files to Client/Domain/
- **Complexity**: Low
- **Estimated Time**: 15 minutes
- **Files to Create/Modify**:
  - Create `Agenix.TestPlatform.Client/Domain/` folder
  - Create `Agenix.TestPlatform.Client/Domain/Events/` folder
  - Move 18 `.cs` files from Domain to Client/Domain/
- **Dependencies**: Task 1
- **Implementation Steps**:
  1. Create directories:
     ```bash
     mkdir -p Agenix.TestPlatform.Client/Domain
     mkdir -p Agenix.TestPlatform.Client/Domain/Events
     ```
  2. Move files with git:
     ```bash
     git mv Agenix.TestPlatform.Domain/LabelKey.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/LabelKeyParsingOptions.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/LabelMatchingOptions.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/LabelMatcher.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/ILabelMatcher.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/Launch.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/LaunchStatus.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/LaunchFilter.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/ProjectsUsers.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/RememberMeToken.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/RunContracts.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/RunNameRules.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/RedisKeys.cs Agenix.TestPlatform.Client/Domain/
     git mv Agenix.TestPlatform.Domain/Events/TestItemEvent.cs Agenix.TestPlatform.Client/Domain/Events/
     git mv Agenix.TestPlatform.Domain/Events/LogItemEvent.cs Agenix.TestPlatform.Client/Domain/Events/
     git mv Agenix.TestPlatform.Domain/Events/CommandEvent.cs Agenix.TestPlatform.Client/Domain/Events/
     git mv Agenix.TestPlatform.Domain/Events/AuditEvent.cs Agenix.TestPlatform.Client/Domain/Events/
     git mv Agenix.TestPlatform.Domain/Events/ArtifactEvents.cs Agenix.TestPlatform.Client/Domain/Events/
     ```
  3. Verify files moved: `ls -R Agenix.TestPlatform.Client/Domain/`
- **Verification**:
  - [ ] All 13 domain files in Client/Domain/
  - [ ] All 5 event files in Client/Domain/Events/
  - [ ] Git history preserved (use `git log --follow`)
  - [ ] Domain folder nearly empty (only .csproj and obj/ remain)

---

#### Task 3: Update Namespaces in Moved Files
- **Complexity**: Low
- **Estimated Time**: 15 minutes
- **Files to Create/Modify**:
  - All 18 `.cs` files in `Agenix.TestPlatform.Client/Domain/`
- **Dependencies**: Task 2
- **Implementation Steps**:
  1. Use global find & replace in `Agenix.TestPlatform.Client/Domain/`:
     ```regex
     Find:    namespace Agenix\.PlaywrightGrid\.Domain;
     Replace: namespace Agenix.TestPlatform.Client.Domain;
     ```
  2. Update event namespaces:
     ```regex
     Find:    namespace Agenix\.PlaywrightGrid\.Domain\.Events;
     Replace: namespace Agenix.TestPlatform.Client.Domain.Events;
     ```
  3. Build Client project to verify: `dotnet build Agenix.TestPlatform.Client/Agenix.TestPlatform.Client.csproj`
- **Verification**:
  - [ ] All namespace declarations updated (18 files)
  - [ ] No `Agenix.PlaywrightGrid.Domain` references in Client/Domain/
  - [ ] Client project builds successfully (0 errors)

---

#### Task 4: Update Project References
- **Complexity**: Medium
- **Estimated Time**: 15 minutes
- **Files to Create/Modify**:
  - `hub/PlaywrightHub.csproj`
  - `worker/WorkerService.csproj`
  - `dashboard/Dashboard.csproj`
  - `ingestion/IngestionService.csproj`
  - `Agenix.TestPlatform.Integration.Tests/Agenix.TestPlatform.Integration.Tests.csproj`
  - `Agenix.TestPlatform.Domain.Tests/Agenix.TestPlatform.Domain.Tests.csproj`
  - `Dashboard.Tests/Dashboard.Tests.csproj`
  - `WorkerService.Tests/WorkerService.Tests.csproj`
  - `tests/GridTests.csproj`
- **Dependencies**: Task 3
- **Implementation Steps**:
  1. For each project, remove Domain reference:
     ```xml
     <!-- REMOVE -->
     <ProjectReference Include="..\Agenix.TestPlatform.Domain\Agenix.TestPlatform.Domain.csproj" />
     ```
  2. Ensure Client reference exists (if not already present):
     ```xml
     <!-- ENSURE EXISTS -->
     <ProjectReference Include="..\Agenix.TestPlatform.Client\Agenix.TestPlatform.Client.csproj" />
     ```
  3. Verify solution loads: Open solution in Rider, check for errors
- **Verification**:
  - [ ] All 9 projects updated
  - [ ] No projects reference Domain anymore
  - [ ] All projects have Client reference (directly or transitively)
  - [ ] Solution loads without errors in Rider

---

#### Task 5: Update Using Statements
- **Complexity**: Medium
- **Estimated Time**: 30 minutes
- **Files to Create/Modify**:
  - All `.cs` files in hub/, worker/, dashboard/, ingestion/, test projects with Domain using statements
  - Estimated 50-80 files across 9 projects
- **Dependencies**: Task 4
- **Implementation Steps**:
  1. Global find & replace across entire solution:
     ```regex
     Find:    using Agenix\.PlaywrightGrid\.Domain;
     Replace: using Agenix.TestPlatform.Client.Domain;
     ```
  2. Update event using statements:
     ```regex
     Find:    using Agenix\.PlaywrightGrid\.Domain\.Events;
     Replace: using Agenix.TestPlatform.Client.Domain.Events;
     ```
  3. Build entire solution: `dotnet build`
  4. Fix any remaining build errors manually
- **Verification**:
  - [ ] No `using Agenix.PlaywrightGrid.Domain` references remain
  - [ ] All using statements updated (50-80 files)
  - [ ] `dotnet build` succeeds (0 errors)
  - [ ] No warnings about missing types

---

#### Task 6: Delete Domain Project
- **Complexity**: Low
- **Estimated Time**: 5 minutes
- **Files to Create/Modify**:
  - Delete `Agenix.TestPlatform.Domain/` folder
  - Update `AgenixTestPlatform.sln` (remove Domain project)
- **Dependencies**: Task 5
- **Implementation Steps**:
  1. Delete Domain project folder:
     ```bash
     git rm -r Agenix.TestPlatform.Domain/
     ```
  2. Remove from solution file:
     - Open `AgenixTestPlatform.sln` in text editor
     - Remove project entry for `Agenix.TestPlatform.Domain`
     - Or use Rider: Right-click project → Remove from solution
  3. Reload solution in Rider
- **Verification**:
  - [ ] Domain folder deleted
  - [ ] Domain project removed from solution
  - [ ] Solution loads without errors
  - [ ] `dotnet build` still succeeds

---

#### Task 7: Rename Domain.Tests Project
- **Complexity**: Medium
- **Estimated Time**: 10 minutes
- **Files to Create/Modify**:
  - `Agenix.TestPlatform.Domain.Tests/` → `Agenix.TestPlatform.Client.Domain.Tests/` (rename folder)
  - `Agenix.TestPlatform.Domain.Tests.csproj` → `Agenix.TestPlatform.Client.Domain.Tests.csproj` (rename)
  - Update namespaces in test files
  - Update solution file
- **Dependencies**: Task 6
- **Implementation Steps**:
  1. Rename project folder:
     ```bash
     git mv Agenix.TestPlatform.Domain.Tests/ Agenix.TestPlatform.Client.Domain.Tests/
     ```
  2. Rename .csproj file:
     ```bash
     cd Agenix.TestPlatform.Client.Domain.Tests/
     git mv Agenix.TestPlatform.Domain.Tests.csproj Agenix.TestPlatform.Client.Domain.Tests.csproj
     ```
  3. Update namespaces in test files:
     ```regex
     Find:    namespace Agenix\.TestPlatform\.Domain\.Tests
     Replace: namespace Agenix.TestPlatform.Client.Domain.Tests
     ```
  4. Update assembly name in .csproj:
     ```xml
     <AssemblyName>Agenix.TestPlatform.Client.Domain.Tests</AssemblyName>
     <RootNamespace>Agenix.TestPlatform.Client.Domain.Tests</RootNamespace>
     ```
  5. Update solution file project reference
  6. Build: `dotnet build Agenix.TestPlatform.Client.Domain.Tests/`
- **Verification**:
  - [ ] Project folder renamed
  - [ ] .csproj file renamed
  - [ ] Namespaces updated in test files
  - [ ] Assembly name updated
  - [ ] Solution loads project correctly
  - [ ] Project builds successfully

---

#### Task 8: Final Build & Test Verification
- **Complexity**: Medium
- **Estimated Time**: 20 minutes
- **Files to Create/Modify**: None (verification only)
- **Dependencies**: Task 7
- **Implementation Steps**:
  1. Clean solution: `dotnet clean`
  2. Build solution: `dotnet build`
  3. Run all unit tests: `dotnet test`
  4. Run domain tests specifically: `dotnet test Agenix.TestPlatform.Client.Domain.Tests/`
  5. Start services: `./scripts/run-local-dev-inline.sh`
  6. Manual smoke test:
     - Open dashboard
     - Create launch
     - Create test item
     - Verify browser borrowing works
  7. Stop services
- **Verification**:
  - [ ] `dotnet build` succeeds (0 errors)
  - [ ] All unit tests pass
  - [ ] Domain tests pass (renamed project)
  - [ ] Services start without errors
  - [ ] Dashboard loads
  - [ ] Can create launches and test items
  - [ ] Browser pool functionality works

---

#### Task 9: Git Commit & Cleanup
- **Complexity**: Low
- **Estimated Time**: 10 minutes
- **Files to Create/Modify**: None (Git operations only)
- **Dependencies**: Task 8
- **Implementation Steps**:
  1. Review all changes: `git status`
  2. Stage all changes: `git add -A`
  3. Commit with message:
     ```
     Refactor: Merge Domain into Client package

     - Moved 18 domain files from Agenix.TestPlatform.Domain to Agenix.TestPlatform.Client/Domain/
     - Updated namespaces: Agenix.TestPlatform.Domain → Agenix.TestPlatform.Client.Domain
     - Updated 9 projects to remove Domain reference, use Client only
     - Updated using statements in 50+ files across solution
     - Deleted Agenix.TestPlatform.Domain project
     - Renamed Domain.Tests → Client.Domain.Tests
     - Verified: Build succeeds, all tests pass, services run

     Breaking Changes:
     - Users must update using statements to Agenix.TestPlatform.Client.Domain
     - Remove Domain package reference (only Client needed now)

     Migration: Single NuGet package (Agenix.TestPlatform.Client) contains all types

     Task: SDD Stage 3 - Merge Domain into Client
     Estimated: 2 hours | Actual: [X hours]
     ```
  4. Optional: Delete backup branch if not needed
  5. Push to remote (if applicable)
- **Verification**:
  - [ ] All changes committed
  - [ ] Commit message follows SDD convention
  - [ ] Feature branch ready for merge
  - [ ] No uncommitted files

---

### Execution Strategy

**Phase 1: File Operations (Tasks 1-3)** - 35 minutes
- Backup, move files, update namespaces
- Focus: File system structure and Client project isolation
- Expected: Client builds, other projects have errors (expected)

**Phase 2: Dependency Updates (Tasks 4-5)** - 45 minutes
- Update project references and using statements
- Focus: Project dependencies and namespace resolution
- Expected: Full solution builds by end of phase

**Phase 3: Cleanup (Tasks 6-7)** - 15 minutes
- Delete Domain project, rename Domain.Tests
- Focus: Remove obsolete artifacts
- Expected: Solution structure finalized

**Phase 4: Verification (Tasks 8-9)** - 30 minutes
- Build, test, smoke test, commit
- Focus: Quality assurance and documentation
- Expected: Feature complete and production-ready

**Total Estimated Time**: 2 hours 5 minutes (conservative estimate)

### Rollback Plan

If issues arise during implementation:

- **After Task 1-3**: Revert git commits, files moved but isolated from rest of solution
- **After Task 4-5**: Use `git reset --hard` to previous working commit
- **After Task 6-7**: Can restore Domain project from git history if needed
- **After Task 8-9**: Feature complete, no rollback needed (only documentation updates remain)

### Risk Mitigation

1. **Build Failures**: Commit after Tasks 3, 5, 6, 7 for incremental rollback points
2. **Missed References**: Global search after Task 5 to find remaining Domain references
3. **Test Failures**: Stop immediately at Task 8, investigate before continuing
4. **Service Issues**: Smoke test in Task 8 validates runtime behavior

---

**Date**: 2025-12-27
**Status**: Ready for Implementation
