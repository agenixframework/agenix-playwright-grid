# SDD Stage 2: Architecture Planning - Merge Domain into Client

## Architecture: Merge Domain into Client Package

### Research: Existing Patterns

**Similar Refactorings in Project History**:
- ReportPortal Model Migration (2025-01-25) - Consolidated multiple tables, renamed namespaces
- Test Run → Test Item Migration - Large-scale namespace changes with backward compatibility
- Suite ID → Parent Item ID (2025-01-13) - Comprehensive rename across 50+ files

**Relevant Patterns**:
- **DDD Layer Boundaries**: Domain layer should be internal to bounded context
- **Single Package Distribution**: NuGet packages should bundle related concerns
- **Namespace Hierarchy**: Use `ParentNamespace.SubNamespace` for logical grouping

### Approach 1: Simple File Move + Namespace Change

**Description**: Move all Domain files to Client/Domain/ folder and update namespaces

**Implementation**:
- Use `git mv` to move files preserving history
- Update namespace declarations in moved files
- Update using statements in all referencing projects
- Delete Domain project and update solution file

**Pros**:
- ✅ Simple and straightforward
- ✅ Git preserves file history
- ✅ Clear folder structure
- ✅ Fast execution (1-2 hours)

**Cons**:
- ❌ Manual namespace updates (could miss some)
- ❌ Requires updating 9 projects

**Complexity**: Low

---

### Approach 2: IDE Refactoring with Move

**Description**: Use Rider's refactoring tools to move and rename namespaces automatically

**Implementation**:
- Drag Domain folder into Client project in Rider
- Use "Adjust Namespace" refactoring
- Let IDE update all references automatically
- Manually delete Domain project

**Pros**:
- ✅ IDE handles namespace updates
- ✅ Type-safe refactoring
- ✅ Automatic using statement updates

**Cons**:
- ❌ IDE may not preserve git history correctly
- ❌ Requires manual verification of changes
- ❌ Could miss files outside IDE's scope

**Complexity**: Medium

---

### Approach 3: Hybrid (Manual Move + Global Replace)

**Description**: Manually move files with git, then use global find/replace for namespaces

**Implementation**:
- Use `git mv` to preserve history
- Use global find/replace for namespace changes
- Use global find/replace for using statements
- Verify with build after each step

**Pros**:
- ✅ Git history preserved
- ✅ Fast text replacement
- ✅ Repeatable and auditable

**Cons**:
- ❌ Risk of over-replacement (need careful regex)
- ❌ Manual verification required

**Complexity**: Low

---

### Recommendation: Approach 3 (Hybrid)

**Justification**:
- **Git History**: Preserving file history is critical for long-term maintenance
- **Speed**: Global replace is faster than IDE refactoring for 9 projects
- **Auditability**: Text-based changes are easier to review in PR
- **Safety**: Can verify changes incrementally with build
- **Precedent**: Successfully used in previous migrations (Parent Item ID, ReportPortal)

**Risks**:
- Risk: Accidentally replace namespace in comments/strings
  → Mitigation: Use precise regex, verify changes before commit

- Risk: Miss files outside main source tree (e.g., test fixtures)
  → Mitigation: Global search after completion to find remaining references

- Risk: Break build with incomplete namespace updates
  → Mitigation: Build after each phase, fix immediately

### Detailed Implementation Plan

#### Phase 1: File Move (15 minutes)

**Move Domain files to Client/Domain/**:

```bash
# Create Domain folder in Client
mkdir -p Agenix.TestPlatform.Client/Domain
mkdir -p Agenix.TestPlatform.Client/Domain/Events

# Move domain files (preserving history)
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

# Move event files
git mv Agenix.TestPlatform.Domain/Events/TestItemEvent.cs Agenix.TestPlatform.Client/Domain/Events/
git mv Agenix.TestPlatform.Domain/Events/LogItemEvent.cs Agenix.TestPlatform.Client/Domain/Events/
git mv Agenix.TestPlatform.Domain/Events/CommandEvent.cs Agenix.TestPlatform.Client/Domain/Events/
git mv Agenix.TestPlatform.Domain/Events/AuditEvent.cs Agenix.TestPlatform.Client/Domain/Events/
git mv Agenix.TestPlatform.Domain/Events/ArtifactEvents.cs Agenix.TestPlatform.Client/Domain/Events/
```

#### Phase 2: Update Namespaces in Moved Files (15 minutes)

**Find & Replace in Client/Domain/ files**:

```regex
Find:    namespace Agenix\.PlaywrightGrid\.Domain;
Replace: namespace Agenix.TestPlatform.Client.Domain;

Find:    namespace Agenix\.PlaywrightGrid\.Domain\.Events;
Replace: namespace Agenix.TestPlatform.Client.Domain.Events;
```

**Verification**: Build Client project - should succeed (isolated from other projects)

#### Phase 3: Update Project References (15 minutes)

**Remove Domain, ensure Client references exist**:

Projects to update:
1. `hub/PlaywrightHub.csproj`
2. `worker/WorkerService.csproj`
3. `dashboard/Dashboard.csproj`
4. `ingestion/IngestionService.csproj`
5. `Agenix.TestPlatform.Integration.Tests/`
6. `Agenix.TestPlatform.Domain.Tests/`
7. `Dashboard.Tests/`
8. `WorkerService.Tests/`
9. `tests/GridTests.csproj`

**For each project**:
```xml
<!-- REMOVE THIS -->
<ProjectReference Include="..\Agenix.TestPlatform.Domain\Agenix.TestPlatform.Domain.csproj" />

<!-- ENSURE THIS EXISTS (may already be there) -->
<ProjectReference Include="..\Agenix.TestPlatform.Client\Agenix.TestPlatform.Client.csproj" />
```

#### Phase 4: Update Using Statements (30 minutes)

**Global find & replace across ALL projects**:

```regex
Find:    using Agenix\.PlaywrightGrid\.Domain;
Replace: using Agenix.TestPlatform.Client.Domain;

Find:    using Agenix\.PlaywrightGrid\.Domain\.Events;
Replace: using Agenix.TestPlatform.Client.Domain.Events;
```

**Files to update** (estimated 50-80 files across 9 projects)

#### Phase 5: Delete Domain Project (5 minutes)

```bash
# Remove Domain project folder
rm -rf Agenix.TestPlatform.Domain/

# Remove from solution file
# (Edit AgenixTestPlatform.sln manually or use Rider)
```

#### Phase 6: Rename Domain.Tests (10 minutes)

**Option A**: Rename to `Agenix.TestPlatform.Client.Domain.Tests`
**Option B**: Merge into `Agenix.TestPlatform.Client.Tests`

**Recommendation**: Option A (keep separate for clarity)

```bash
git mv Agenix.TestPlatform.Domain.Tests/ Agenix.TestPlatform.Client.Domain.Tests/
# Update .csproj file name
# Update namespace in test files
```

#### Phase 7: Build & Test Verification (20 minutes)

```bash
# Clean build
dotnet clean
dotnet build

# Run all tests
dotnet test

# Run integration tests
./scripts/run-local-dev-inline.sh
# Manual smoke test
```

### Contracts

#### Namespace Mapping

**Before → After**:
```
Agenix.PlaywrightGrid.Domain
  → Agenix.TestPlatform.Client.Domain

Agenix.PlaywrightGrid.Domain.Events
  → Agenix.TestPlatform.Client.Domain.Events
```

#### Folder Structure

**Before**:
```
Solution/
├── Agenix.TestPlatform.Client/
│   ├── Abstractions/
│   ├── Resources/
│   └── ...
└── Agenix.TestPlatform.Domain/
    ├── LabelKey.cs
    ├── Events/
    └── ...
```

**After**:
```
Solution/
└── Agenix.TestPlatform.Client/
    ├── Abstractions/
    ├── Resources/
    └── Domain/              ← NEW
        ├── LabelKey.cs
        ├── Events/
        └── ...
```

#### Project References

**Before**:
```xml
<ProjectReference Include="..\Agenix.TestPlatform.Domain\Agenix.TestPlatform.Domain.csproj" />
<ProjectReference Include="..\Agenix.TestPlatform.Client\Agenix.TestPlatform.Client.csproj" />
```

**After**:
```xml
<ProjectReference Include="..\Agenix.TestPlatform.Client\Agenix.TestPlatform.Client.csproj" />
```

### Dependencies

**No New Dependencies**: Client already has all needed packages

**Removed Dependencies**: Domain project (zero external dependencies)

**Build Tools**:
- Git for file moves
- Global find/replace (IDE or command-line tools)
- .NET SDK for build verification

### Execution Timeline

**Total Estimated Time**: 2 hours

| Phase | Task | Duration |
|-------|------|----------|
| 1 | File move with git | 15 min |
| 2 | Update namespaces in moved files | 15 min |
| 3 | Update project references | 15 min |
| 4 | Update using statements | 30 min |
| 5 | Delete Domain project | 5 min |
| 6 | Rename Domain.Tests | 10 min |
| 7 | Build & test verification | 20 min |
| - | **Buffer** | 10 min |

### Rollback Plan

**After Phase 1-2**: Revert git commits (files moved but isolated)
**After Phase 3-4**: Build failures easy to fix (compile-time errors)
**After Phase 5-6**: Can restore Domain project from git history
**After Phase 7**: Feature complete, no rollback needed

---

**Date**: 2025-12-27
**Status**: Ready for Task Breakdown
