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

using System.Net;

namespace Agenix.PlaywrightGrid.Client;

/// <summary>
///     Exception thrown when a PlaywrightGrid service operation fails.
/// </summary>
public class ServiceException : Exception
{
    private readonly string? _message;

    /// <summary>
    ///     Creates a new service exception.
    /// </summary>
    public ServiceException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a new service exception with an inner exception.
    /// </summary>
    public ServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a new service exception with full HTTP request/response context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="httpMethod">The HTTP method.</param>
    /// <param name="responseBody">The response body content.</param>
    public ServiceException(string message, HttpStatusCode statusCode, Uri requestUri, HttpMethod httpMethod,
        string responseBody)
        : base(message)
    {
        HttpStatusCode = statusCode;
        RequestUri = requestUri;
        HttpMethod = httpMethod;
        ResponseBody = responseBody;
        _message = $"{message}\n {statusCode} ({(int)statusCode}) {httpMethod} {requestUri}\n {responseBody}";
    }

    /// <summary>
    ///     HTTP status code if applicable.
    /// </summary>
    public int? StatusCode => (int?)HttpStatusCode;

    /// <summary>
    ///     Gets the HTTP status code of the failed request.
    /// </summary>
    public HttpStatusCode? HttpStatusCode { get; init; }

    /// <summary>
    ///     Gets the request URI that failed.
    /// </summary>
    public Uri? RequestUri { get; init; }

    /// <summary>
    ///     Gets the HTTP method used in the failed request.
    /// </summary>
    public HttpMethod? HttpMethod { get; init; }

    /// <summary>
    ///     Gets the response body content.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    ///     Response content if available (backwards compatibility).
    /// </summary>
    public string? ResponseContent => ResponseBody;

    /// <summary>
    ///     Gets the detailed error message including request/response context.
    /// </summary>
    public override string Message => _message ?? base.Message;
}
