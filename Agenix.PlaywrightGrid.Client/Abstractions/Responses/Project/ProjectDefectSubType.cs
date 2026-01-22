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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses.Project;

/// <summary>
///     Represents a defect sub-type in a project.
/// </summary>
public class ProjectDefectSubType
{
    /// <summary>
    ///     Gets or sets the ID of the defect sub-type.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    ///     Gets or sets the color associated with the defect sub-type.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    ///     Gets or sets the locator of the defect sub-type.
    /// </summary>
    public string Locator { get; set; }

    /// <summary>
    ///     Gets or sets the long name of the defect sub-type.
    /// </summary>
    public string LongName { get; set; }

    /// <summary>
    ///     Gets or sets the short name of the defect sub-type.
    /// </summary>
    public string ShortName { get; set; }
}
