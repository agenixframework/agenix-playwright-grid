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

namespace WorkerService.Application.Ports;

public interface IStateRepository
{
    Task ListRightPushAsync(string listKey, string itemJson);
    Task<long> ListLengthAsync(string listKey);
    Task<IReadOnlyList<string>> ListRangeAsync(string listKey);
    Task<long> ListRemoveAsync(string listKey, string itemJson, long count = 0);

    Task HashSetAsync(string key, string field, string value);
    Task SetAddAsync(string key, string member);
    Task StringSetAsync(string key, string value, TimeSpan? expiry);
    Task<bool> SetRemoveAsync(string key, string member);
}
