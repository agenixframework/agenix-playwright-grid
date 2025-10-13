# SDD Stage 1: Merge Domain into Client Specification

## Feature: Merge Agenix.TestPlatform.Domain into Agenix.TestPlatform.Client

### Overview
Consolidate the `Agenix.TestPlatform.Domain` project into `Agenix.TestPlatform.Client` to create a single NuGet package that users need to reference. This simplifies the client library distribution and reduces dependency complexity.

### User Stories

**As a** test automation developer
**I want to** install a single NuGet package for the Agenix Test Platform client
**So that** I don't have to manage multiple package dependencies

**As a** library maintainer
**I want to** reduce the number of projects in the solution
**So that** the codebase is simpler to maintain and version

**As a** NuGet package consumer
**I want to** see all types I need in one package
**So that** I can use the client library without understanding internal architecture

### Acceptance Criteria

- [ ] All `.cs` files from `Agenix.TestPlatform.Domain/` moved to `Agenix.TestPlatform.Client/`
- [ ] `Agenix.TestPlatform.Domain` project deleted
- [ ] All projects referencing Domain now reference Client instead
- [ ] Namespaces updated: `Agenix.TestPlatform.Domain.*` → `Agenix.TestPlatform.Client.Domain.*`
- [ ] Build succeeds with 0 errors
- [ ] All tests pass
- [ ] Single NuGet package: `Agenix.TestPlatform.Client`

### Constraints

- **Technical**: Must maintain backward compatibility for namespace changes (provide using aliases if needed)
- **Performance**: No impact on runtime performance
- **Compatibility**: Existing code using Domain types should still work with minimal changes
- **Timeline**: Single session refactoring (1-2 hours estimated)

### Out of Scope

- Changing the internal organization of Domain types (they remain as-is, just relocated)
- Refactoring Domain logic (this is purely a merge operation)
- Creating new abstractions or interfaces

### Current Structure Analysis

#### Agenix.TestPlatform.Domain Project

**Purpose**: Domain models and business logic (DDD-style domain layer)

**Files** (18 source files):
1. `LabelKey.cs` - Value object for capacity routing keys (App:Browser:Env)
2. `LabelKeyParsingOptions.cs` - Configuration for label parsing
3. `LabelMatchingOptions.cs` - Configuration for label matching
4. `LabelMatcher.cs` - Business logic for matching labels
5. `ILabelMatcher.cs` - Interface for label matching
6. `Launch.cs` - Launch domain entity
7. `LaunchStatus.cs` - Launch status enum
8. `LaunchFilter.cs` - Launch filtering logic
9. `ProjectsUsers.cs` - Projects/Users domain models
10. `RememberMeToken.cs` - Authentication token model
11. `RunContracts.cs` - Run-related contracts
12. `RunNameRules.cs` - Business rules for run names
13. `RedisKeys.cs` - Redis key constants
14. `Events/TestItemEvent.cs` - Test item domain event
15. `Events/LogItemEvent.cs` - Log item domain event
16. `Events/CommandEvent.cs` - Command domain event
17. `Events/AuditEvent.cs` - Audit domain event
18. `Events/ArtifactEvents.cs` - Artifact domain events

**Dependencies**: ZERO external dependencies (pure domain layer)

**Referenced By**: 9 projects
- `hub/PlaywrightHub.csproj` ✅ Hub service (should reference Client after merge)
- `worker/WorkerService.csproj` ✅ Worker service (should reference Client after merge)
- `dashboard/Dashboard.csproj` ✅ Dashboard (should reference Client after merge)
- `ingestion/IngestionService.csproj` ✅ Ingestion service (should reference Client after merge)
- `Agenix.TestPlatform.Integration.Tests` ✅ Integration tests
- `Agenix.TestPlatform.Domain.Tests` ⚠️ Will be renamed to Client.Tests or merged
- `Dashboard.Tests` ✅ Dashboard tests
- `WorkerService.Tests` ✅ Worker tests
- `tests/GridTests.csproj` ✅ Grid tests

#### Agenix.TestPlatform.Client Project

**Purpose**: HTTP client library for API communication

**Files** (70+ source files):
- API resources (Launch, TestItem, LogItem, Project, User, UserFilter)
- Request/Response DTOs
- Models (enums, attributes)
- Converters (JSON serialization)
- Extensions
- Examples

**Dependencies**:
- `Microsoft.Extensions.Http.Resilience`
- `Polly.Extensions.Http`
- `System.Text.Json`
- `Microsoft.Extensions.Http`
- `Microsoft.Extensions.Options.ConfigurationExtensions`
- `Polly`

**Current Structure**:
```
Agenix.TestPlatform.Client/
├── Abstractions/
│   ├── Filtering/
│   ├── Models/
│   ├── Requests/
│   ├── Responses/
│   └── Resources/
├── Converters/
├── Examples/
├── Extensions/
└── Resources/
```

### Proposed Merge Strategy

**Move Domain files into Client with namespace preservation:**

```
Agenix.TestPlatform.Client/
├── Abstractions/        (existing)
├── Converters/          (existing)
├── Examples/            (existing)
├── Extensions/          (existing)
├── Resources/           (existing)
└── Domain/              (NEW - merged from Domain project)
    ├── LabelKey.cs
    ├── LabelMatcher.cs
    ├── ILabelMatcher.cs
    ├── Launch.cs
    ├── LaunchStatus.cs
    ├── LaunchFilter.cs
    ├── ProjectsUsers.cs
    ├── RememberMeToken.cs
    ├── RunContracts.cs
    ├── RunNameRules.cs
    ├── RedisKeys.cs
    └── Events/
        ├── TestItemEvent.cs
        ├── LogItemEvent.cs
        ├── CommandEvent.cs
        ├── AuditEvent.cs
        └── ArtifactEvents.cs
```

**Namespace Changes**:
- **Old**: `namespace Agenix.TestPlatform.Domain;`
- **New**: `namespace Agenix.TestPlatform.Client.Domain;`
- **Old**: `namespace Agenix.TestPlatform.Domain.Events;`
- **New**: `namespace Agenix.TestPlatform.Client.Domain.Events;`

### Success Metrics

- 100% of Domain files migrated to Client
- 0 projects still referencing Domain project
- 0 build errors after merge
- All tests pass (integration, unit, domain)
- Single NuGet package with all types

### Rationale

**Why merge Domain into Client?**

1. **Simplified Distribution**: Users install one package, not two
2. **DDD Principles**: Domain layer should be internal to the bounded context (the client library IS the bounded context for external consumers)
3. **Reduced Complexity**: Fewer projects to manage, version, and publish
4. **Namespace Clarity**: `Agenix.TestPlatform.Client.Domain` clearly indicates domain models within the client library
5. **Precedent**: Many libraries (e.g., NServiceBus, MediatR) include domain models in the main package

**Why NOT keep them separate?**

- ❌ **Two packages to version**: More complexity in release management
- ❌ **Circular dependency risk**: Client needs Domain, but Domain shouldn't need Client
- ❌ **User confusion**: Which package do I install? Do I need both?
- ❌ **No reusability**: Domain is NOT shared across multiple bounded contexts (only used by Client)

### Migration Path for Existing Users

**For users upgrading from older versions**:

1. **Remove Domain reference**:
   ```xml
   <!-- OLD -->
   <PackageReference Include="Agenix.TestPlatform.Domain" Version="1.0.0" />
   <PackageReference Include="Agenix.TestPlatform.Client" Version="1.0.0" />

   <!-- NEW -->
   <PackageReference Include="Agenix.TestPlatform.Client" Version="2.0.0" />
   ```

2. **Update using statements** (if using Domain types directly):
   ```csharp
   // OLD
   using Agenix.TestPlatform.Domain;
   using Agenix.TestPlatform.Domain.Events;

   // NEW
   using Agenix.TestPlatform.Client.Domain;
   using Agenix.TestPlatform.Client.Domain.Events;
   ```

3. **No runtime changes**: All functionality remains identical

### Risk Assessment

**Low Risk**:
- Pure structural refactor, no logic changes
- Domain has zero dependencies, so no package conflicts
- Namespace changes are compile-time breaking (easy to fix)
- Tests will validate functionality

**Mitigation**:
- Create backup branch before starting
- Commit after each phase
- Run tests after each major step
- Use IDE refactoring tools for namespace changes

### Alternatives Considered

#### Alternative 1: Keep Separate Projects

**Pros**:
- ✅ Clear separation of concerns
- ✅ Can version independently (rarely needed)

**Cons**:
- ❌ Two packages to manage
- ❌ User confusion
- ❌ No real benefit (Domain not shared elsewhere)

**Decision**: REJECTED - Adds complexity without benefit

#### Alternative 2: Merge into Shared Library

**Pros**:
- ✅ Could be shared across multiple client libraries

**Cons**:
- ❌ No other client libraries exist
- ❌ Creates third package (even more complexity)
- ❌ YAGNI violation

**Decision**: REJECTED - Premature optimization

#### Alternative 3: Merge with Namespace Alias (Recommended)

**Pros**:
- ✅ Single package
- ✅ Clear namespace hierarchy
- ✅ Can provide backward-compatible using aliases if needed

**Cons**:
- ❌ Breaking change for users directly using Domain types (minor impact)

**Decision**: ACCEPTED - Best balance of simplicity and clarity

### Stakeholder Sign-Off

- [ ] User approves merging Domain into Client
- [ ] User approves namespace change to `Agenix.TestPlatform.Client.Domain`
- [ ] User approves deletion of Domain project
- [ ] User confirms single NuGet package requirement

---

**Date**: 2025-12-27
**Status**: Draft - Awaiting User Approval
