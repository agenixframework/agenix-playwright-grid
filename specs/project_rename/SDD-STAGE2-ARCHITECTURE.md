# SDD Stage 2: Architecture Planning - Project Rename

## Architecture: Rename to "Agenix Test Platform"

### Research: Existing Patterns

**Similar Refactorings in Project History**:
- Suite ID → Parent Item ID migration (2025-01-13) - Comprehensive rename across codebase
- ReportPortal Model Database Migration (2025-01-25) - Large-scale schema consolidation
- Test Run → Test Item migration - Namespace and API changes

**Relevant Patterns**:
- **Find & Replace Strategy**: Systematic text replacement across file types
- **Build Verification**: Compile after each phase to catch issues early
- **Test-First**: Run tests to validate no functional regressions
- **Git Branch Strategy**: Feature branch with incremental commits

### Approach 1: Manual Find & Replace

**Description**: Use IDE/editor find & replace to manually update each occurrence

**Implementation**:
- Use Rider/VS Code global find & replace
- Manually review each occurrence before replacing
- Rename files and folders using file system operations

**Pros**:
- ✅ Full control over each change
- ✅ Can skip intentional exceptions (e.g., git history references)
- ✅ No tooling dependencies

**Cons**:
- ❌ Time-consuming (100+ file changes)
- ❌ Error-prone (easy to miss files)
- ❌ Tedious for large codebase

**Complexity**: Medium

---

### Approach 2: Automated Script-Based Rename

**Description**: Write shell/PowerShell script to automate find & replace and file renames

**Implementation**:
- Script uses `sed`, `find`, `mv` commands
- Processes all file types: `.cs`, `.csproj`, `.sln`, `.md`, `.yml`, `.sh`
- Renames files and folders programmatically

**Pros**:
- ✅ Fast execution (< 1 minute)
- ✅ Repeatable and auditable
- ✅ Can be version controlled

**Cons**:
- ❌ Risk of over-replacement (e.g., in URLs, external references)
- ❌ Requires testing and validation
- ❌ May not handle edge cases well

**Complexity**: Medium

---

### Approach 3: Hybrid (IDE Refactoring + Manual Verification)

**Description**: Use IDE refactoring tools for namespace changes, manual find & replace for everything else

**Implementation**:
- Use Rider's "Rename" refactoring for namespaces
- Use "Find in Files" for documentation and config
- Manually rename solution and project files
- Git commit after each phase for rollback safety

**Pros**:
- ✅ IDE handles complex namespace updates
- ✅ Type-safe (compiler catches errors)
- ✅ Incremental with rollback points

**Cons**:
- ❌ Slower than fully automated
- ❌ Still requires manual file renames

**Complexity**: Low

---

### Recommendation: Approach 3 (Hybrid)

**Justification**:
- **Safety First**: IDE refactoring is type-safe for C# namespaces
- **DDD Alignment**: Respects layer boundaries (IDE understands dependencies)
- **Incremental**: Can commit after each phase for rollback
- **Best of Both**: Automation where safe, manual control where needed
- **Proven Pattern**: Used successfully in Test Item migration

**Risks**:
- Risk: Miss files in edge locations (scripts/, specs/)
  → Mitigation: Use global find to verify all occurrences updated

- Risk: Break build with incomplete rename
  → Mitigation: Build after each phase, fix immediately

- Risk: Test failures due to hardcoded strings
  → Mitigation: Run test suite after namespace changes

### Contracts

#### Solution & Project Files

**Before**:
```
PlaywrightGrid.sln
Agenix.PlaywrightGrid.Client/
Agenix.PlaywrightGrid.Domain/
Agenix.PlaywrightGrid.Shared/
Agenix.PlaywrightGrid.Integration.Tests/
PlaywrightHub.Tests/
WorkerService.Tests/
Dashboard.Tests/
```

**After**:
```
AgenixTestPlatform.sln
Agenix.TestPlatform.Client/
Agenix.TestPlatform.Domain/
Agenix.TestPlatform.Shared/
Agenix.TestPlatform.Integration.Tests/
Agenix.TestPlatform.Hub.Tests/
Agenix.TestPlatform.Worker.Tests/
Agenix.TestPlatform.Dashboard.Tests/
```

#### Namespace Changes

**Before**:
```csharp
namespace Agenix.PlaywrightGrid.Client;
namespace Agenix.PlaywrightGrid.Domain;
namespace Agenix.PlaywrightGrid.Shared;
namespace PlaywrightHub.Application;
namespace PlaywrightHub.Infrastructure;
```

**After**:
```csharp
namespace Agenix.TestPlatform.Client;
namespace Agenix.TestPlatform.Domain;
namespace Agenix.TestPlatform.Shared;
namespace Agenix.TestPlatform.Hub.Application;
namespace Agenix.TestPlatform.Hub.Infrastructure;
```

#### Documentation Changes

**Files to Update**:
- `CLAUDE.md` - All references to "Agenix Playwright Grid"
- `README.md` - Project title and description
- `docs/**/*.md` - All documentation files
- `.env` - Comments only (variable names unchanged)
- `docker-compose.yml` - Comments only (service names unchanged)

**Pattern**:
```markdown
# BEFORE
Agenix Playwright Grid Dashboard
Agenix Playwright Service

# AFTER
Agenix Test Platform Dashboard
Agenix Test Platform
```

#### Configuration Changes (Backward Compatible)

**No Changes to These**:
- Environment variable names: `AGENIX_HUB_*`, `AGENIX_WORKER_*` (remain unchanged)
- Docker service names: `hub`, `worker`, `dashboard` (remain unchanged)
- Database table names: `launches`, `test_items` (remain unchanged)
- API endpoint paths: `/api/launches`, `/api/test-items` (remain unchanged)

**Comments Only Updates**:
```yaml
# BEFORE
# Agenix Playwright Grid Hub Service
hub:
  image: agenix-playwright-grid-hub

# AFTER
# Agenix Test Platform Hub Service
hub:
  image: agenix-test-platform-hub  # Image name is changed as well
```

### Dependencies

**External Libraries**: None - pure naming refactor

**Build Tools**:
- Rider IDE for namespace refactoring
- Git for version control and rollback

**Infrastructure**: No changes - deployment remains identical

### Execution Phases

**Phase 1: Solution & Project Files (30 min)**
- Rename `.sln` file
- Rename `.csproj` files
- Rename project folders
- Update solution references
- Build to verify

**Phase 2: C# Namespaces (45 min)**
- Use Rider "Rename" on namespace declarations
- Update `using` statements
- Update assembly names in `.csproj`
- Build to verify

**Phase 3: Documentation (30 min)**
- Update CLAUDE.md
- Update README.md
- Update all files in `docs/`
- Update comments in shell scripts

**Phase 4: Configuration Comments (15 min)**
- Update `.env` comments
- Update `docker-compose.yml` comments
- Update Dockerfile comments

**Phase 5: Verification (30 min)**
- Full solution build
- Run unit tests
- Run integration tests (optional)
- Manual smoke test
- Git commit

**Total Estimated Time**: 2.5 hours

---

**Date**: 2025-12-27
**Status**: Ready for Task Breakdown
