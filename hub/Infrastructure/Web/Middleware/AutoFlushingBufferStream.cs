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

using Microsoft.AspNetCore.Http;

namespace PlaywrightHub.Infrastructure.Web.Middleware;

/// <summary>
/// A stream that conditionally buffers the response body based on the status code and content type.
/// It avoids buffering successful responses (2xx/3xx) or responses that are already ProblemDetails,
/// mitigating performance concerns for large successful responses.
/// </summary>
public class AutoFlushingBufferStream(Stream originalStream, HttpContext context) : Stream
{
    private readonly Stream _originalStream = originalStream;
    private readonly HttpContext _context = context;
    private MemoryStream? _buffer;
    private bool? _useBuffer;

    /// <summary>
    /// Gets a value indicating whether the response is currently being buffered.
    /// </summary>
    public bool IsBuffered => _useBuffer == true;

    /// <summary>
    /// Gets the buffer containing the response body if buffering is enabled.
    /// </summary>
    public MemoryStream? Buffer => _buffer;

    /// <summary>
    /// Ensures that the buffering state is initialized based on the current response status and content type.
    /// This is automatically called on the first write, but can be called manually if no data is written.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_useBuffer.HasValue) return;

        var status = _context.Response.StatusCode;
        var contentType = _context.Response.ContentType ?? string.Empty;
        _ = contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase);

        // Buffer all error responses (>= 400) to allow for potential normalization or enhancement.
        // Successful responses (2xx/3xx) are written directly to the original stream.
        _useBuffer = status >= 400;
        if (_useBuffer.Value)
        {
            _buffer = new MemoryStream();
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _useBuffer == true ? _buffer!.Length : _originalStream.Length;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
        if (_useBuffer == true)
        {
            _buffer!.Flush();
        }
        else
        {
            try
            {
                _originalStream.Flush();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Synchronous operations are disallowed"))
            {
                // Note: We avoid propagating this exception from Flush() because it's a common Kestrel constraint
                // when AllowSynchronousIO is false (default). In a web context, a synchronous flush is often
                // called by internal components (like ResponseCaching) and can be safely ignored if the
                // underlying stream doesn't support it, as long as FlushAsync is used for real work.
            }
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_useBuffer == true) return _buffer!.FlushAsync(cancellationToken);
        return _originalStream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value)
    {
        if (_useBuffer == true) _buffer!.SetLength(value);
        else _originalStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureInitialized();
        if (_useBuffer!.Value) _buffer!.Write(buffer, offset, count);
        else _originalStream.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        if (_useBuffer!.Value) await _buffer!.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        else await _originalStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_useBuffer!.Value) await _buffer!.WriteAsync(buffer, cancellationToken);
        else await _originalStream.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_buffer != null)
        {
            await _buffer.DisposeAsync();
        }
        await base.DisposeAsync();
    }
}
