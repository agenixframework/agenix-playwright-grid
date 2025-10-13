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

namespace Agenix.PlaywrightGrid.Domain.Events;

/// <summary>
///     Event published when an artifact upload is requested.
///     The ingestion service consumes this event and handles actual storage (local or MinIO).
/// </summary>
public record ArtifactUploadEvent(
    Guid ArtifactId,
    Guid TestItemId,
    string FileName,
    string ContentType,
    long FileSize,
    byte[] Content,
    DateTime UploadedAt,
    string ProjectKey
);
