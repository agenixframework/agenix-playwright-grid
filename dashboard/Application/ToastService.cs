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

using System.Collections.Concurrent;

namespace Dashboard.Application;

public enum ToastLevel { Info, Success, Warning, Error }

public sealed class ToastMessage
{
    public ToastMessage(ToastLevel level, string message, int timeoutMs)
    {
        Id = Guid.NewGuid();
        Level = level;
        Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        TimeoutMs = timeoutMs <= 0 ? 5000 : timeoutMs;
    }

    public Guid Id { get; }
    public ToastLevel Level { get; }
    public string Message { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public int TimeoutMs { get; }
}

/// <summary>
///     Simple in-circuit toast service for Blazor Server. Keeps a small sliding window of messages
///     and notifies UI on changes. Auto-dismisses messages after timeout.
/// </summary>
public sealed class ToastService
{
    private const int MaxItems = 5; // avoid unbounded growth per circuit
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, ToastMessage> _messages = new();

    public IReadOnlyList<ToastMessage> Current =>
        _messages.Values
            .OrderBy(m => m.CreatedUtc)
            .ToList();

    public event Action? Changed;

    private void Add(ToastMessage msg)
    {
        // Trim if necessary
        lock (_gate)
        {
            if (_messages.Count >= MaxItems)
            {
                var oldest = _messages.Values.OrderBy(m => m.CreatedUtc).FirstOrDefault();
                if (oldest is not null)
                {
                    _messages.TryRemove(oldest.Id, out _);
                }
            }

            _messages[msg.Id] = msg;
        }

        NotifyChanged();
        _ = AutoDismissAsync(msg.Id, msg.TimeoutMs);
    }

    private async Task AutoDismissAsync(Guid id, int timeoutMs)
    {
        try { await Task.Delay(timeoutMs); }
        catch { }

        Dismiss(id);
    }

    public void Dismiss(Guid id)
    {
        _messages.TryRemove(id, out _);
        NotifyChanged();
    }

    public void Clear()
    {
        _messages.Clear();
        NotifyChanged();
    }

    public void Info(string message, int timeoutMs = 5000)
    {
        Add(new ToastMessage(ToastLevel.Info, message, timeoutMs));
    }

    public void Success(string message, int timeoutMs = 5000)
    {
        Add(new ToastMessage(ToastLevel.Success, message, timeoutMs));
    }

    public void Warning(string message, int timeoutMs = 5000)
    {
        Add(new ToastMessage(ToastLevel.Warning, message, timeoutMs));
    }

    public void Error(string message, int timeoutMs = 6000)
    {
        Add(new ToastMessage(ToastLevel.Error, message, timeoutMs));
    }

    private void NotifyChanged()
    {
        try { Changed?.Invoke(); }
        catch { }
    }
}
