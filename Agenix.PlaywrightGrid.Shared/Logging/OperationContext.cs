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

using System.Diagnostics;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Ambient operation context for correlating logs within a single request/operation/job.
///     Uses AsyncLocal storage to flow context across async boundaries.
/// </summary>
public sealed class OperationContext
{
    private static readonly AsyncLocal<OperationContext?> _current = new();

    public OperationContext(
        string operationName,
        Guid? parentOperationId = null,
        Dictionary<string, object>? properties = null,
        Guid? rootOperationId = null,
        int depth = 0)
    {
        OperationId = Guid.NewGuid();
        ParentOperationId = parentOperationId;
        RootOperationId = rootOperationId ?? (parentOperationId == null ? OperationId : Guid.Empty);
        OperationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        StartTime = DateTimeOffset.UtcNow;
        Properties = properties ?? [];
        KeyEvents = [];
        Depth = depth;

        // Capture Activity trace/span IDs if available
        var activity = Activity.Current;
        if (activity != null)
        {
            TraceId = activity.TraceId.ToString();
            SpanId = activity.SpanId.ToString();
        }
    }

    /// <summary>
    ///     Gets or sets the current operation context for this async flow.
    /// </summary>
    public static OperationContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    ///     Unique identifier for this operation (one per request/job/command).
    /// </summary>
    public Guid OperationId { get; }

    /// <summary>
    ///     Parent operation ID for nested operations (e.g., sub-requests, nested transactions).
    /// </summary>
    public Guid? ParentOperationId { get; }

    /// <summary>
    ///     Nesting depth of the operation (0 for root).
    /// </summary>
    public int Depth { get; }

    /// <summary>
    ///     Root operation ID for the entire tree of operations.
    ///     Used for chunked logging to keep all nested operations together.
    /// </summary>
    public Guid RootOperationId { get; set; }

    /// <summary>
    ///     Returns true if this is the root operation of the tree.
    /// </summary>
    public bool IsRootOperation => OperationId == RootOperationId;

    /// <summary>
    ///     Name of the operation (e.g., "StartTestItem", "BorrowBrowser", "ProcessBatch").
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    ///     Distributed trace ID from Activity (OpenTelemetry/W3C TraceContext).
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    ///     Distributed span ID from Activity (OpenTelemetry).
    /// </summary>
    public string? SpanId { get; }

    /// <summary>
    ///     When the operation started (UTC).
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    ///     Additional operation-level properties (user ID, project key, etc.).
    /// </summary>
    public Dictionary<string, object> Properties { get; }

    /// <summary>
    ///     Key milestone event codes logged during this operation (e.g., ["ITEM01", "POOL03"]).
    /// </summary>
    public List<string> KeyEvents { get; }

    /// <summary>
    ///     Creates a new operation context and sets it as the current ambient context.
    ///     Returns an IDisposable that restores the previous context on disposal.
    /// </summary>
    public static IDisposable Begin(
        string operationName,
        Guid? parentOperationId = null,
        Dictionary<string, object>? properties = null)
    {
        var previous = Current;
        var context = new OperationContext(
            operationName,
            parentOperationId ?? previous?.OperationId,
            properties,
            previous?.RootOperationId,
            (previous?.Depth ?? -1) + 1);
        Current = context;

        return new OperationScope(previous);
    }

    /// <summary>
    ///     Records a milestone event code (e.g., "ITEM01", "POOL10") for summary tracking.
    /// </summary>
    public void RecordKeyEvent(string eventCode)
    {
        if (!string.IsNullOrWhiteSpace(eventCode) && !KeyEvents.Contains(eventCode))
        {
            KeyEvents.Add(eventCode);
        }
    }

    /// <summary>
    ///     Calculates the duration since the operation started.
    /// </summary>
    public TimeSpan GetDuration()
    {
        return DateTimeOffset.UtcNow - StartTime;
    }

    private sealed class OperationScope(OperationContext? previous) : IDisposable
    {
        public void Dispose()
        {
            Current = previous;
        }
    }
}
