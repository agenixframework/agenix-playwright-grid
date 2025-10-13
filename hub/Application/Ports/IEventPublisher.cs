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

using Agenix.PlaywrightGrid.Domain.Events;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Event publisher for async processing of high-volume operations.
/// </summary>
public interface IEventPublisher
{
    Task PublishTestItemEventAsync(TestItemEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);
    Task PublishTestItemEventAsync(TestItemAutoStoppedEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);

    Task PublishCommandEventAsync(CommandEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);

    Task PublishLogItemEventAsync(LogItemEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);

    Task PublishAuditEventAsync(AuditEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);

    Task PublishArtifactUploadEventAsync(ArtifactUploadEvent evt, Guid? parentOperationId = null, CancellationToken ct = default);
}
