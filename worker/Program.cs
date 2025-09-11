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

using WorkerService.Services;
using WorkerService.Tools;

namespace WorkerService;

/// <summary>
///     The Program class serves as the entry point for the WorkerService application.
///     This class invokes the asynchronous execution of the service.
/// </summary>
public static class Program
{
    public static Task Main(string[] args)
    {
        // Load local .env variables for developer convenience (no-op if DISABLE_DOTENV=1)
        WorkerService.Infrastructure.DotEnv.Load();

        // CLI subcommand: validate-pool-config [--pool "..."] [--json]
        if (args.Length > 0 && string.Equals(args[0], "validate-pool-config", StringComparison.OrdinalIgnoreCase))
        {
            var code = PoolConfigValidator.Run(args);
            Environment.ExitCode = code;
            return Task.CompletedTask;
        }

        return new WorkerServiceRunner().RunAsync(args);
    }
}
