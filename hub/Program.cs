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

using PlaywrightHub.Services;

namespace PlaywrightHub;

/// <summary>
///     Entry point of the PlaywrightHub application.
/// </summary>
/// <remarks>
///     The <c>Program</c> class is responsible for initializing and invoking the
///     <c>HubServiceRunner</c> to start the application. This is done via the <c>Main</c> method,
///     which takes any arguments provided during execution and passes them to the service.
/// </remarks>
public static class Program
{
    /// <summary>
    ///     The entry point of the application.
    /// </summary>
    /// <param name="args">An array of command-line arguments passed to the application.</param>
    /// <returns>A task that represents the asynchronous operation of initializing and starting the application.</returns>
    public static Task Main(string[] args)
    {
        // Load local .env variables for developer convenience (no-op if DISABLE_DOTENV=1)
        PlaywrightHub.Infrastructure.DotEnv.Load();
        return HubServiceRunner.RunAsync(args);
    }
}
