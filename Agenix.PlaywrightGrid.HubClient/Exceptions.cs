#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
///     Thrown when the hub indicates that capacity is temporarily unavailable for the requested label.
///     Typical HTTP status: 503 Service Unavailable.
/// </summary>
public sealed class CapacityUnavailableException : Exception
{
    public CapacityUnavailableException()
    {
    }

    public CapacityUnavailableException(string message) : base(message)
    {
    }

    public CapacityUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
///     Thrown when the hub rejects the request due to authentication/authorization failure.
///     Typical HTTP statuses: 401 Unauthorized or 403 Forbidden.
/// </summary>
public sealed class AuthenticationException : Exception
{
    public AuthenticationException()
    {
    }

    public AuthenticationException(string message) : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
///     Thrown when the hub returns a response that violates the expected protocol contract
///     (e.g., missing fields, unexpected shape, or unrecognized status codes).
/// </summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException()
    {
    }

    public ProtocolException(string message) : base(message)
    {
    }

    public ProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
