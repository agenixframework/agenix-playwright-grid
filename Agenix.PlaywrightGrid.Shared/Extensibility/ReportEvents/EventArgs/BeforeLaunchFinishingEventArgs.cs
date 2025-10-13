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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Shared.Configuration;
using ReportPortal.Client.Abstractions;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;

/// <summary>
///     Represents the event arguments for the BeforeLaunchFinishing event.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="BeforeLaunchFinishingEventArgs" /> class.
/// </remarks>
/// <param name="clientService">The client service.</param>
/// <param name="configuration">The configuration.</param>
/// <param name="finishLaunchRequest">The finish launch request.</param>
public class BeforeLaunchFinishingEventArgs(IClientService clientService, IConfiguration configuration,
    FinishLaunchRequest finishLaunchRequest) : ReportEventBaseArgs(clientService, configuration)
{

    /// <summary>
    ///     Gets the finish launch request.
    /// </summary>
    public FinishLaunchRequest FinishLaunchRequest { get; } = finishLaunchRequest;
}
