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

using System.Text.Json;

namespace Agenix.PlaywrightGrid.Client.Converters;

internal class ModelSerializer
{
    public static async Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return (T)await JsonSerializer.DeserializeAsync(stream, typeof(T), ClientSourceGenerationContext.Default,
            cancellationToken);
    }

    public static async Task SerializeAsync<T>(object obj, Stream stream, CancellationToken cancellationToken = default)
    {
        await JsonSerializer.SerializeAsync(stream, obj, typeof(T), ClientSourceGenerationContext.Default,
            cancellationToken);
    }
}
