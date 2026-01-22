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

using Agenix.PlaywrightGrid.Shared.Logging;
using HousekeepingService.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Moq;
using NUnit.Framework;

namespace HousekeepingService.Tests.Infrastructure;

[TestFixture]
public class MinioStorageServiceTests
{
    private Mock<IConfiguration> _configMock;
    private ChunkedLogger<MinioStorageService> _logger;
    private Mock<IMinioClient> _minioClientMock;
    private MinioStorageService _service;

    [SetUp]
    public void SetUp()
    {
        _configMock = new Mock<IConfiguration>();
        _minioClientMock = new Mock<IMinioClient>();

        var loggerMock = new Mock<ILogger<MinioStorageService>>();
        _logger = new ChunkedLogger<MinioStorageService>(loggerMock.Object);

        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);
        _configMock.Setup(x => x["MINIO_BUCKET_NAME"]).Returns("test-bucket");

        _service = new MinioStorageService(_configMock.Object, _logger, _minioClientMock.Object);
    }

    [Test]
    public async Task UploadArtifactAsync_ShouldCallPutObjectAsync()
    {
        // Arrange
        var objectKey = "test/path/file.txt";
        var fileData = "hello"u8.ToArray();
        var contentType = "text/plain";

        // Act
        var result = await _service.UploadArtifactAsync(objectKey, fileData, contentType);

        // Assert
        Assert.That(result, Is.EqualTo(objectKey));
        _minioClientMock.Verify(
            x => x.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DeleteArtifactAsync_ShouldCallRemoveObjectAsync()
    {
        // Arrange
        var objectKey = "test/path/file.txt";

        // Act
        await _service.DeleteArtifactAsync(objectKey);

        // Assert
        _minioClientMock.Verify(
            x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task EnsureBucketExistsAsync_WhenBucketDoesNotExist_ShouldCreateBucket()
    {
        // Arrange
        _minioClientMock.Setup(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(false);

        // Act
        await _service.EnsureBucketExistsAsync();

        // Assert
        _minioClientMock.Verify(
            x => x.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task EnsureBucketExistsAsync_WhenBucketExists_ShouldNotCreateBucket()
    {
        // Arrange
        _minioClientMock.Setup(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(true);

        // Act
        await _service.EnsureBucketExistsAsync();

        // Assert
        _minioClientMock.Verify(
            x => x.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
