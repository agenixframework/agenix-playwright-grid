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

namespace HousekeepingService.Infrastructure;

public interface IMinioStorageService : IDisposable
{
    Task<string> UploadArtifactAsync(string objectKey, byte[] fileData, string contentType, CancellationToken ct = default);
    Task<byte[]> DownloadArtifactAsync(string objectKey, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string objectKey, int expirySeconds = 3600, bool inline = false, string? fileName = null, CancellationToken ct = default);
    Task DeleteArtifactAsync(string objectKey, CancellationToken ct = default);
    Task EnsureBucketExistsAsync(CancellationToken ct = default);
}
