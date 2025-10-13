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

using System.Globalization;
using System.Text;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <summary>
///     Occurs when retry request execution is unsuccessful.
/// </summary>
/// <remarks>
///     Initializes a new instance of <see cref="RetryExecutionException" />
/// </remarks>
/// <param name="methodName">Name of the method.</param>
/// <param name="innerExceptions">Inner exceptions.</param>
public class RetryExecutionException(string methodName, IEnumerable<Exception> innerExceptions) : AggregateException(null, innerExceptions)
{
    private readonly string _message = $"'Invocation of '{methodName}' has not been finished.";

    /// <inheritdoc />
    public override string Message => _message;

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder();
        text.AppendLine($"{GetType().Name}: {Message}");
        text.Append(StackTrace);

        for (var index = 0; index < InnerExceptions.Count; index++)
        {
            text.Append(Environment.NewLine + " ---> ");
            text.AppendFormat(CultureInfo.InvariantCulture, "(Inner Exception #{0}) ", index);
            text.Append(InnerExceptions[index]);

            text.Append(" <--- ");
            text.AppendLine();
        }

        return text.ToString();
    }
}
