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

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using PlaywrightHub.Infrastructure.Web.Middleware;
using Xunit;

namespace PlaywrightHub.Tests.Infrastructure.Web.Middleware;

public class AutoFlushingBufferStreamTests
{
    private readonly Mock<Stream> _originalStreamMock;
    private readonly DefaultHttpContext _httpContext;
    private readonly AutoFlushingBufferStream _stream;

    public AutoFlushingBufferStreamTests()
    {
        _originalStreamMock = new Mock<Stream>();
        _originalStreamMock.Setup(s => s.CanWrite).Returns(true);
        _httpContext = new DefaultHttpContext();
        _stream = new AutoFlushingBufferStream(_originalStreamMock.Object, _httpContext);
    }

    [Fact]
    public void Write_WhenStatusIsSuccess_ShouldWriteToOriginalStream()
    {
        // Arrange
        _httpContext.Response.StatusCode = 200;
        var data = Encoding.UTF8.GetBytes("Success data");

        // Act
        _stream.Write(data, 0, data.Length);

        // Assert
        _stream.IsBuffered.Should().BeFalse();
        _originalStreamMock.Verify(s => s.Write(data, 0, data.Length), Times.Once);
    }

    [Fact]
    public void Write_WhenStatusIsError_ShouldBuffer()
    {
        // Arrange
        _httpContext.Response.StatusCode = 400;
        var data = Encoding.UTF8.GetBytes("Error data");

        // Act
        _stream.Write(data, 0, data.Length);

        // Assert
        _stream.IsBuffered.Should().BeTrue();
        _stream.Buffer.Should().NotBeNull();
        _stream.Buffer!.Length.Should().Be(data.Length);
        _originalStreamMock.Verify(s => s.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Write_WhenStatusIsErrorButAlreadyProblemDetails_ShouldStillBufferForEnhancement()
    {
        // Arrange
        _httpContext.Response.StatusCode = 400;
        _httpContext.Response.ContentType = "application/problem+json";
        var data = Encoding.UTF8.GetBytes("{\"title\":\"Problem\"}");

        // Act
        _stream.Write(data, 0, data.Length);

        // Assert
        // We now buffer all errors (>= 400) to allow the middleware to inject eventCode/traceId
        _stream.IsBuffered.Should().BeTrue();
        _stream.Buffer.Should().NotBeNull();
        _originalStreamMock.Verify(s => s.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task WriteAsync_WhenStatusIsSuccess_ShouldWriteToOriginalStream()
    {
        // Arrange
        _httpContext.Response.StatusCode = 200;
        var data = Encoding.UTF8.GetBytes("Success data");

        // Act
        await _stream.WriteAsync(data, 0, data.Length);

        // Assert
        _stream.IsBuffered.Should().BeFalse();
        _originalStreamMock.Verify(s => s.WriteAsync(data, 0, data.Length, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Flush_WhenOriginalStreamThrowsSyncIORestriction_ShouldIgnoreException()
    {
        // Arrange
        _httpContext.Response.StatusCode = 200; // No buffering
        _originalStreamMock.Setup(s => s.Flush()).Throws(new InvalidOperationException("Synchronous operations are disallowed. Call WriteAsync or set AllowSynchronousIO to true instead."));
        _stream.Write(new byte[] { 1 }, 0, 1); // Initialize

        // Act
        Action act = () => _stream.Flush();

        // Assert
        act.Should().NotThrow<InvalidOperationException>();
        _originalStreamMock.Verify(s => s.Flush(), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_WhenStatusIsSuccess_ShouldCallOriginalStreamFlushAsync()
    {
        // Arrange
        _httpContext.Response.StatusCode = 200;
        _stream.Write(new byte[] { 1 }, 0, 1); // Initialize

        // Act
        await _stream.FlushAsync();

        // Assert
        _originalStreamMock.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EnsureInitialized_ShouldSetIsBufferedCorrectly_EvenIfNoWrite()
    {
        // Arrange
        _httpContext.Response.StatusCode = 404;

        // Act
        _stream.EnsureInitialized();

        // Assert
        _stream.IsBuffered.Should().BeTrue();
        _stream.Buffer.Should().NotBeNull();
    }
}
