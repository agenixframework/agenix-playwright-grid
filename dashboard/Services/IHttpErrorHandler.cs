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

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dashboard.Models;

namespace Dashboard.Services;

/// <summary>
/// Service interface for centralized HTTP error handling and UI notification.
/// </summary>
public interface IHttpErrorHandler
{
    /// <summary>
    /// HandleExceptionAsync converts an exception + optional request into an ErrorDetails.
    /// </summary>
    /// <param name="ex">The exception to handle.</param>
    /// <param name="request">The optional HTTP request that caused the exception.</param>
    /// <returns>A task that represents the asynchronous operation, containing the <see cref="ErrorDetails"/>.</returns>
    Task<ErrorDetails> HandleExceptionAsync(Exception ex, HttpRequestMessage? request);

    /// <summary>
    /// ShowErrorAsync triggers UI error display (modal).
    /// </summary>
    /// <param name="error">The error details to display.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ShowErrorAsync(ErrorDetails error);

    /// <summary>
    /// IsRetryableAsync determines whether the exception should be retried.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>A task that represents the asynchronous operation, containing true if the error is retryable; otherwise, false.</returns>
    Task<bool> IsRetryableAsync(Exception ex);
}
