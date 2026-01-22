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

namespace ReportPortal.Client;

/// <summary>
///     Occurs when server cannot process a request.
/// </summary>
public class ServiceException : Exception
{
    private readonly string _message;

    /// <summary>
    ///     Initializes a new instance of <see cref="ServiceException" /> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="httpStatusCode">Response HTTP status code.</param>
    /// <param name="requestUri">Request Uri.</param>
    /// <param name="httpMethod">HTTP method.</param>
    /// <param name="responseBody">Response body.</param>
    public ServiceException(string message, HttpStatusCode httpStatusCode, Uri requestUri, HttpMethod httpMethod,
        string responseBody)
    {
        HttpStatusCode = httpStatusCode;
        RequestUri = requestUri;
        HttpMethod = httpMethod;
        ResponseBody = responseBody;

        _message = $"{message}\n {httpStatusCode} ({(int)httpStatusCode}) {httpMethod} {requestUri}\n {responseBody}";
    }

    /// <summary>
    ///     Gets HTTP status code.
    /// </summary>
    public HttpStatusCode HttpStatusCode { get; }

    /// <summary>
    ///     Gets request uri.
    /// </summary>
    public Uri RequestUri { get; }

    /// <summary>
    ///     Gets HTTP method.
    /// </summary>
    public HttpMethod HttpMethod { get; }

    /// <summary>
    ///     Gets response body.
    /// </summary>
    public string ResponseBody { get; }

    /// <inheritdoc />
    public override string Message => _message;
}
