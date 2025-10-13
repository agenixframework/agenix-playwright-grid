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

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Enriches log events with the current OperationContext data (OperationId, ParentOperationId, TraceId, etc.).
/// </summary>
public sealed class OperationContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = OperationContext.Current;
        if (context == null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("OperationId", context.OperationId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RootOperationId", context.RootOperationId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("IsRootOperation", context.IsRootOperation));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("OperationName", context.OperationName));

        if (context.ParentOperationId.HasValue)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentOperationId",
                context.ParentOperationId.Value));
        }

        if (context.TraceId != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", context.TraceId));
        }

        if (context.SpanId != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", context.SpanId));
        }

        // Add operation properties
        foreach (var kvp in context.Properties)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(kvp.Key, kvp.Value));
        }
    }
}

/// <summary>
///     Enriches log events with code context (caller file path, line number, member name).
///     Note: This requires CallerAttributes which are compile-time, so we'll use SourceContext instead.
/// </summary>
public sealed class CodeContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Extract source context if present (already added by Serilog)
        if (logEvent.Properties.TryGetValue("SourceContext", out _))
        {
            // SourceContext is already present, no additional work needed
            // In real scenarios, we'd need compile-time attributes for FilePath:LineNumber
            // For now, we rely on SourceContext being set by Serilog's logger factory
        }
    }
}

/// <summary>
///     Enriches log events with a default EventCode if not already present.
///     Individual log calls should provide EventCode, but this ensures there's always a value.
/// </summary>
public sealed class EventCodeEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Only add default EventCode if not already present
        if (!logEvent.Properties.ContainsKey("EventCode"))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("EventCode", EventCodes.Generic));
        }
    }
}

/// <summary>
///     Extension methods for registering enrichers with Serilog configuration.
/// </summary>
public static class SerilogEnricherExtensions
{
    /// <summary>
    ///     Adds OperationContext enricher to Serilog configuration.
    /// </summary>
    public static LoggerConfiguration WithOperationContext(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With<OperationContextEnricher>();
    }

    /// <summary>
    ///     Adds CodeContext enricher to Serilog configuration.
    /// </summary>
    public static LoggerConfiguration WithCodeContext(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With<CodeContextEnricher>();
    }

    /// <summary>
    ///     Adds EventCode enricher to Serilog configuration.
    /// </summary>
    public static LoggerConfiguration WithEventCode(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With<EventCodeEnricher>();
    }
}
