# Merge Domain into Client - SDD Documentation

## Overview

This specification documents the merge of `Agenix.TestPlatform.Domain` into `Agenix.TestPlatform.Client` to create a single NuGet package for users.

## Problem Statement

Currently, users must reference two packages:
- `Agenix.TestPlatform.Domain` (18 files, zero dependencies)
- `Agenix.TestPlatform.Client` (70+ files, 6 NuGet dependencies)

This creates unnecessary complexity in package management and distribution.

## Solution

Merge Domain into Client with clear namespace hierarchy:
- Old: `Agenix.TestPlatform.Domain`
- New: `Agenix.TestPlatform.Client.Domain`

Result: Single NuGet package (`Agenix.TestPlatform.Client`) containing all types.

## SDD Stages

### ✅ Stage 1: Specification (COMPLETE)
- **File**: `SDD-STAGE1-SPECIFICATION.md`
- **Content**: User stories, acceptance criteria, current structure analysis, rationale
- **Status**: Approved

### ✅ Stage 2: Architecture Planning (COMPLETE)
- **File**: `SDD-STAGE2-ARCHITECTURE.md`
- **Content**: Three approaches analyzed, hybrid approach recommended, detailed implementation plan
- **Status**: Approved

### ✅ Stage 3: Task Breakdown (COMPLETE)
- **File**: `SDD-STAGE3-TASKS.md`
- **Content**: 9 tasks with dependencies, verification criteria, execution strategy
- **Status**: Ready for Implementation

### 🔄 Stage 4: Implementation (NEXT)
- **Status**: Awaiting execution approval
- **Estimated Time**: 2 hours
- **Tasks**: 9 tasks from file move to final verification

### 📚 Stage 5: Documentation (PENDING)
- **Status**: Will update CLAUDE.md after implementation
- **Content**: Merge details, migration guide, breaking changes

## Key Metrics

**Files Affected**:
- 18 domain files moved
- 9 projects updated (references)
- 50-80 files updated (using statements)
- 1 project deleted (Domain)
- 1 project renamed (Domain.Tests → Client.Domain.Tests)

**Impact**:
- ✅ Single NuGet package for users
- ✅ Simplified dependency management
- ✅ Clear namespace hierarchy
- ⚠️ Breaking change: Using statements must be updated

**Timeline**:
- Planning: 30 minutes (complete)
- Implementation: 2 hours (pending)
- Documentation: 15 minutes (pending)
- **Total**: 2.75 hours

## Breaking Changes

### For Package Consumers

**Before**:
```xml
<PackageReference Include="Agenix.TestPlatform.Domain" Version="1.0.0" />
<PackageReference Include="Agenix.TestPlatform.Client" Version="1.0.0" />
```

```csharp
using Agenix.TestPlatform.Domain;
using Agenix.TestPlatform.Domain.Events;
```

**After**:
```xml
<PackageReference Include="Agenix.TestPlatform.Client" Version="2.0.0" />
```

```csharp
using Agenix.TestPlatform.Client.Domain;
using Agenix.TestPlatform.Client.Domain.Events;
```

## Migration Guide

1. Remove `Agenix.TestPlatform.Domain` package reference
2. Update using statements (compile-time errors will guide you)
3. All functionality remains identical (no runtime changes)

## Benefits

1. **Single Package**: Users install one package, not two
2. **Simpler Versioning**: One version number to track
3. **Clearer Namespace**: `Client.Domain` shows relationship
4. **DDD Aligned**: Domain is internal to bounded context
5. **Industry Standard**: Similar to MediatR, NServiceBus patterns

## Risks & Mitigation

| Risk | Mitigation |
|------|------------|
| Build failures | Incremental commits after each phase |
| Missed references | Global search verification |
| Test failures | Stop immediately, investigate |
| Git history loss | Use `git mv` to preserve history |

## Next Steps

**To Execute Implementation**:
1. Review Task 1-9 in `SDD-STAGE3-TASKS.md`
2. Create backup branch
3. Execute tasks sequentially
4. Verify after each phase
5. Commit with SDD message template

**After Implementation**:
1. Update `CLAUDE.md` Recent Changes section
2. Create migration guide for users
3. Update package release notes
4. Increment major version (breaking change)

---

**Date**: 2025-12-27
**Author**: Claude Code (SDD Workflow)
**Status**: Planning Complete - Ready for Implementation
