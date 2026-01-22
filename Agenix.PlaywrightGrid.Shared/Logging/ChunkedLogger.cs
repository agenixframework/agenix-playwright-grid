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

using System.Text;
using Microsoft.Extensions.Logging;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Interface for operation scopes that support milestone logging and structured completion.
/// </summary>
public interface IChunkedOperation : IDisposable
{
    /// <summary>
    ///     Operation context associated with this scope.
    /// </summary>
    OperationContext? Context { get; }

    /// <summary>
    ///     Sets output data for the operation.
    /// </summary>
    void SetOutputs(Dictionary<string, object> outputs);

    /// <summary>
    ///     Marks the operation as completed successfully.
    /// </summary>
    void Complete();

    /// <summary>
    ///     Marks the operation as failed with error details.
    /// </summary>
    void Fail(Exception error, ErrorType errorType, DependencyName? dependency = null);

    /// <summary>
    ///     Marks the operation as failed with error details and outputs.
    /// </summary>
    void Fail(Exception error, ErrorType errorType, Dictionary<string, object>? outputs, DependencyName? dependency = null);
}

/// <summary>
///     Wrapper over ILogger providing operation-scoped chunked logging with event codes and structured properties.
///     Automatically manages OperationContext and enriches logs with correlation IDs, event codes, and code context.
/// </summary>
public class ChunkedLogger(ILogger logger, string categoryName, ChunkedLoggerOptions? options = null)
{
    private readonly string _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ChunkedLoggerOptions _options = options ?? ChunkedLoggerOptions.Default;

    /// <summary>
    ///     Configuration options for this logger.
    /// </summary>
    public ChunkedLoggerOptions Options => _options;

    /// <summary>
    ///     Internal logger instance.
    /// </summary>
    public ILogger Logger => _logger;

    /// <summary>
    ///     Internal constructor for generic version.
    /// </summary>
    protected ChunkedLogger(ILogger logger, ChunkedLoggerOptions? options = null)
        : this(logger, logger.GetType().Name, options)
    {
    }

    /// <summary>
    ///     Begins a new operation scope with automatic start logging and cleanup on disposal.
    ///     Returns an IDisposable that completes the operation (success or failure) when disposed.
    ///     Uses logging scopes to provide structural grouping in log viewers.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "StartTestItem", "BorrowBrowser")</param>
    /// <param name="inputs">Input parameters for the operation (e.g., launchId, labelKey)</param>
    /// <param name="parentOperationId">Optional parent operation ID for nested operations</param>
    /// <returns>IDisposable that completes the operation on disposal</returns>
    public IChunkedOperation BeginOperation(
        string operationName,
        Dictionary<string, object>? inputs = null,
        Guid? parentOperationId = null)
    {
        // If chunked logging is disabled, return a no-op scope
        if (!_options.Enabled)
        {
            return new NoOpScope();
        }

        // Detect parent context and root ID
        var parentContext = OperationContext.Current;
        var effectiveParentId = parentOperationId ?? parentContext?.OperationId;
        var effectiveRootId = parentContext?.RootOperationId;
        var effectiveDepth = (parentContext?.Depth ?? -1) + 1;

        var context =
            new OperationContext(operationName, effectiveParentId, inputs ?? [],
                effectiveRootId, effectiveDepth);

        // Ensure RootOperationId is set if it was null (root operation)
        if (context.RootOperationId == Guid.Empty)
        {
            context.RootOperationId = context.OperationId;
        }

        OperationContext.Current = context;

        // Create scope state for structural grouping in log viewers
        var scopeState = new Dictionary<string, object?>
        {
            ["operation"] = operationName,
            ["operationId"] = context.OperationId,
            ["rootOperationId"] = context.RootOperationId,
            ["isRootOperation"] = context.IsRootOperation,
            ["depth"] = context.Depth
        };

        if (context.ParentOperationId.HasValue)
        {
            scopeState["parentOperationId"] = context.ParentOperationId.Value;
        }

        if (context.TraceId != null)
        {
            scopeState["traceId"] = context.TraceId;
        }

        if (context.SpanId != null)
        {
            scopeState["spanId"] = context.SpanId;
        }

        // Add input properties to scope
        if (inputs != null)
        {
            scopeState["inputs"] = inputs;
        }

        // Begin the logging scope - this provides structural grouping
        var logScope = _logger.BeginScope(scopeState);

        // Log "Begin" for all operations, with indentation for nested ones
        var indent = GetIndent(context.Depth);
        if (!context.ParentOperationId.HasValue)
        {
            _logger.LogInformation("{Indent}╔═ Operation: {OperationName}  OperationId='{OperationId}'",
                indent, operationName, context.OperationId);
        }
        else
        {
            _logger.LogInformation(
                "{Indent}╠═ Nested Operation: {OperationName}  OperationId='{OperationId}' ParentId='{ParentOperationId}'",
                indent, operationName, context.OperationId, context.ParentOperationId.Value);
        }

        var operationScope = new OperationScope(this, context, parentContext);

        // Return a composite disposable that disposes both scopes in reverse order (LIFO)
        // and exposes OperationScope methods for backward compatibility
        return new CompositeDisposable(logScope, operationScope);
    }

    /// <summary>
    ///     Logs a milestone event within the current operation.
    ///     Milestone events are tracked in KeyEvents for summary display.
    /// </summary>
    /// <param name="eventCode">Event code (e.g., "ITEM01", "POOL10")</param>
    /// <param name="message">Log message template</param>
    /// <param name="properties">Structured properties</param>
    public void LogMilestone(
        string eventCode,
        string message,
        params object?[]? properties)
    {
        LogMilestoneInternal(LogLevel.Information, eventCode, null, message, properties);
    }

    /// <summary>
    ///     Logs a milestone event with an exception within the current operation.
    /// </summary>
    public void LogMilestone(
        string eventCode,
        Exception ex,
        string message,
        params object?[]? properties)
    {
        LogMilestoneInternal(LogLevel.Error, eventCode, ex, message, properties);
    }

    /// <summary>
    ///     Logs an information-level event within the current operation chunk.
    /// </summary>
    public void LogInformation(
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            if (!_options.Enabled) return;
            var context = OperationContext.Current;
            var indent = GetIndent(context?.Depth ?? 0);
            _logger.LogInformation($"{indent}║ {message}", properties);
            return;
        }

        LogMilestoneInternal(LogLevel.Information, eventCode, null, message, properties);
    }

    /// <summary>
    ///     Logs a debug-level event within the current operation chunk.
    /// </summary>
    public void LogDebug(
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            if (!_options.Enabled) return;
            var context = OperationContext.Current;
            var indent = GetIndent(context?.Depth ?? 0);
            _logger.LogDebug($"{indent}║ {message}", properties);
            return;
        }

        LogMilestoneInternal(LogLevel.Debug, eventCode, null, message, properties);
    }

    /// <summary>
    ///     Logs a warning-level event within the current operation chunk.
    /// </summary>
    public void LogWarning(
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        LogWarning(null, eventCode, message, properties);
    }

    /// <summary>
    ///     Logs a warning-level event with an exception within the current operation chunk.
    /// </summary>
    public void LogWarning(
        Exception? ex,
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            if (!_options.Enabled) return;
            var context = OperationContext.Current;
            var indent = GetIndent(context?.Depth ?? 0);
            _logger.LogWarning(ex, $"{indent}║ {message}", properties);
            return;
        }

        LogMilestoneInternal(LogLevel.Warning, eventCode, ex, message, properties);
    }

    /// <summary>
    ///     Logs an error-level event within the current operation chunk.
    /// </summary>
    public void LogError(
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        LogError(null, eventCode, message, properties);
    }

    /// <summary>
    ///     Logs an error-level event within the current operation chunk.
    /// </summary>
    public void LogError(
        Exception? ex,
        string? eventCode,
        string message,
        params object?[]? properties)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            if (!_options.Enabled) return;
            var context = OperationContext.Current;
            var indent = GetIndent(context?.Depth ?? 0);
            _logger.LogError(ex, $"{indent}║ {message}", properties);
            return;
        }

        LogMilestoneInternal(LogLevel.Error, eventCode, ex, message, properties);
    }

    private void LogMilestoneInternal(
        LogLevel level,
        string eventCode,
        Exception? ex,
        string message,
        params object?[]? properties)
    {
        // If chunked logging is disabled, skip logging
        if (!_options.Enabled)
        {
            return;
        }

        var context = OperationContext.Current;
        context?.RecordKeyEvent(eventCode);

        var title = EventCodes.GetEventTitle(eventCode);
        var prefix = title == eventCode ? $"[{eventCode}]" : $"[{eventCode}] {title}";

        // Add indentation for nested operations
        var indent = GetIndent(context?.Depth ?? 0);

        // We use a property for MilestonePrefix to ensure clean output without duplicating the EventCode in brackets.
        var template = $"{indent}║ {{MilestonePrefix}} - {message}";

        var props = properties ?? [];
        var args = new object?[props.Length + 1];
        args[0] = prefix;
        Array.Copy(props, 0, args, 1, props.Length);

        // Pass EventCode as a structured property via scope
        using (_logger.BeginScope(new Dictionary<string, object> { ["EventCode"] = eventCode }))
        {
            if (ex != null)
            {
                _logger.Log(level, ex, template, args);
            }
            else
            {
                _logger.Log(level, template, args);
            }
        }
    }

    private static string GetIndent(int depth)
    {
        if (depth <= 0) return "";
        var sb = new StringBuilder(depth * 2);
        for (var i = 0; i < depth; i++)
        {
            sb.Append("║ ");
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Completes the operation successfully with optional output data.
    /// </summary>
    internal void CompleteOperation(OperationContext context, Dictionary<string, object>? outputs = null)
    {
        var duration = context.GetDuration();
        var durationStr = FormatDuration(duration);
        var keyEventsStr = context.KeyEvents.Count > 0
            ? $"  KeyEvents=[{string.Join(",", context.KeyEvents)}]"
            : "";

        var properties = new Dictionary<string, object>
        {
            ["OperationId"] = context.OperationId,
            ["RootOperationId"] = context.RootOperationId,
            ["IsRootOperation"] = context.IsRootOperation,
            ["OperationName"] = context.OperationName,
            ["EventType"] = "OperationEnd",
            ["Status"] = "SUCCESS",
            ["Duration"] = duration.TotalMilliseconds,
            ["DurationDisplay"] = durationStr
        };

        // Add output properties
        if (outputs != null)
        {
            foreach (var kvp in outputs)
            {
                properties[$"Output_{kvp.Key}"] = kvp.Value;
            }
        }

        // Add key events summary
        if (context.KeyEvents.Count > 0)
        {
            properties["KeyEvents"] = string.Join(",", context.KeyEvents);
        }

        using (_logger.BeginScope(properties))
        {
            // We explicitly include key properties in the template to ensure they are captured by LogEvent
            // even if the scope dictionary is not properly enriched (depends on Serilog configuration).
            // These properties are prefixed with @ to be hidden in some formatters or just appended.

            var indent = GetIndent(context.Depth);
            var endSymbol = $"{indent}╚═";

            _logger.LogInformation("{EndSymbol} End: SUCCESS  Duration={DurationDisplay}{KeyEvents} (OpId={OperationId}, Type={EventType})",
                endSymbol, durationStr, keyEventsStr, context.OperationId, "OperationEnd");
        }
    }

    /// <summary>
    ///     Fails the operation with error details, error type, and optional dependency name.
    /// </summary>
    public void FailOperation(
        OperationContext? context,
        Exception error,
        ErrorType errorType,
        DependencyName? dependency = null)
    {
        FailOperation(context, error, errorType, null, dependency);
    }

    /// <summary>
    ///     Fails the operation with error details, error type, outputs, and optional dependency name.
    /// </summary>
    public void FailOperation(
        OperationContext? context,
        Exception error,
        ErrorType errorType,
        Dictionary<string, object>? outputs,
        DependencyName? dependency = null)
    {
        if (context == null) return;

        var duration = context.GetDuration();
        var durationStr = FormatDuration(duration);
        var dependencyStr = dependency.HasValue ? $" Dependency={dependency.Value}" : "";
        var keyEventsStr = context.KeyEvents.Count > 0
            ? $"  KeyEvents=[{string.Join(",", context.KeyEvents)}]"
            : "";

        var properties = new Dictionary<string, object>
        {
            ["OperationId"] = context.OperationId,
            ["RootOperationId"] = context.RootOperationId,
            ["IsRootOperation"] = context.IsRootOperation,
            ["OperationName"] = context.OperationName,
            ["EventType"] = "OperationEnd",
            ["Status"] = "FAILED",
            ["ErrorType"] = errorType.ToString(),
            ["Duration"] = duration.TotalMilliseconds,
            ["DurationDisplay"] = durationStr,
            ["ErrorMessage"] = error.Message
        };

        // Add output properties
        if (outputs != null)
        {
            foreach (var kvp in outputs)
            {
                properties[$"Output_{kvp.Key}"] = kvp.Value;
            }
        }

        if (dependency.HasValue)
        {
            properties["Dependency"] = dependency.Value.ToString();
        }

        // Add key events summary
        if (context.KeyEvents.Count > 0)
        {
            properties["KeyEvents"] = string.Join(",", context.KeyEvents);
        }

        using (_logger.BeginScope(properties))
        {
            var indent = GetIndent(context.Depth);
            var endSymbol = $"{indent}╚═";

            _logger.LogError(error,
                "{EndSymbol} End: FAILED  ErrorType={ErrorType}{Dependency}  Duration={DurationDisplay}{KeyEvents} (OpId={OperationId}, Type={EventType})",
                endSymbol, errorType, dependencyStr, durationStr, keyEventsStr, context.OperationId, "OperationEnd");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
        {
            return $"{duration.TotalMilliseconds:F0}ms";
        }

        if (duration.TotalSeconds < 60)
        {
            return $"{duration.TotalSeconds:F2}s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{duration.TotalMinutes:F1}m";
        }

        return $"{duration.TotalHours:F1}h";
    }

    /// <summary>
    ///     Creates an operation scope for migrations that works before DI is configured.
    ///     Uses ILogger directly since migrations run before the DI container is built.
    /// </summary>
    /// <param name="logger">ILogger instance (from LoggerFactory.Create)</param>
    /// <param name="operationName">Name of the migration operation</param>
    /// <param name="inputs">Optional input parameters</param>
    /// <returns>IDisposable that completes the operation on disposal</returns>
    public static IDisposable BeginMigrationOperation(
        ILogger logger,
        string operationName,
        Dictionary<string, object>? inputs = null)
    {
        return new MigrationOperationScope(logger, operationName, inputs);
    }

    /// <summary>
    ///     Migration operation scope for early startup logging (before DI configured).
    ///     Provides operation tracking with success/failure states and duration tracking.
    /// </summary>
    private sealed class MigrationOperationScope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _operationName;
        private readonly DateTimeOffset _startTime;
        private bool _completed;
        private DependencyName? _dependency;
        private Exception? _error;
        private ErrorType? _errorType;

        public MigrationOperationScope(
            ILogger logger,
            string operationName,
            Dictionary<string, object>? inputs)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _startTime = DateTimeOffset.UtcNow;

            // Log operation start
            var inputProps = inputs != null ? string.Join(", ", inputs.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "";
            var inputStr = !string.IsNullOrWhiteSpace(inputProps) ? $" Inputs=[{inputProps}]" : "";

            _logger.LogInformation("╔═ Migration: {OperationName} Start{Inputs}",
                _operationName, inputStr);
        }

        /// <summary>
        ///     Marks the operation as completed successfully.
        /// </summary>
        public void Complete()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            var duration = DateTimeOffset.UtcNow - _startTime;

            _logger.LogInformation(
                "╚═ Migration: {OperationName} End SUCCESS Duration={Duration:F1}s",
                _operationName, duration.TotalSeconds);
        }

        /// <summary>
        ///     Marks the operation as failed with error details.
        /// </summary>
        public void Fail(Exception ex, ErrorType errorType, DependencyName dependencyName)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _error = ex;
            _errorType = errorType;
            _dependency = dependencyName;

            _logger.LogError(ex,
                "╚═ Migration: {OperationName} End FAILED ErrorType={ErrorType} Dependency={Dependency}",
                _operationName, errorType, dependencyName);
        }

        public void Dispose()
        {
            if (!_completed)
            {
                // Auto-complete on disposal if not explicitly completed/failed
                Complete();
            }
        }
    }

    /// <summary>
    ///     Helper to dispose multiple IDisposable resources in reverse order (LIFO).
    ///     Used to ensure logging scope and operation scope are disposed correctly.
    ///     Exposes OperationScope methods (Fail, Complete, SetOutputs) for backward compatibility.
    /// </summary>
    public sealed class CompositeDisposable(IDisposable logScope, ChunkedLogger.OperationScope operationScope) : IChunkedOperation
    {
        private readonly IDisposable[] _disposables = [logScope, operationScope];
        private readonly OperationScope _operationScope = operationScope ?? throw new ArgumentNullException(nameof(operationScope));

        public OperationContext? Context => _operationScope.Context;

        public void Dispose()
        {
            // Dispose in reverse order (LIFO) to ensure proper cleanup
            for (int i = _disposables.Length - 1; i >= 0; i--)
            {
                _disposables[i]?.Dispose();
            }
        }

        /// <summary>
        ///     Marks the operation as failed. Delegates to the wrapped OperationScope.
        /// </summary>
        public void Fail(Exception error, ErrorType errorType, DependencyName? dependency = null)
        {
            _operationScope.Fail(error, errorType, dependency);
        }

        /// <summary>
        ///     Marks the operation as failed with outputs. Delegates to the wrapped OperationScope.
        /// </summary>
        public void Fail(Exception error, ErrorType errorType, Dictionary<string, object>? outputs, DependencyName? dependency = null)
        {
            _operationScope.Fail(error, errorType, outputs, dependency);
        }

        /// <summary>
        ///     Marks the operation as completed successfully. Delegates to the wrapped OperationScope.
        /// </summary>
        public void Complete()
        {
            _operationScope.Complete();
        }

        /// <summary>
        ///     Sets output data for the operation. Delegates to the wrapped OperationScope.
        /// </summary>
        public void SetOutputs(Dictionary<string, object> outputs)
        {
            _operationScope.SetOutputs(outputs);
        }
    }

    public sealed class OperationScope(
        ChunkedLogger logger,
        OperationContext context,
        OperationContext? previous) : IChunkedOperation
    {
        public OperationContext Context => context;

        private DependencyName? _dependency;
        private bool _disposed;
        private Exception? _error;
        private ErrorType _errorType;
        private Dictionary<string, object>? _outputs;
        private bool _completed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (!_completed)
            {
                if (_error != null)
                {
                    logger.FailOperation(context, _error, _errorType, _outputs, _dependency);
                }
                else
                {
                    logger.CompleteOperation(context, _outputs);
                }
            }

            // Restore previous operation context
            OperationContext.Current = previous;
        }

        /// <summary>
        ///     Sets output data to be included in the operation completion log.
        /// </summary>
        public void SetOutputs(Dictionary<string, object> outputs)
        {
            _outputs = outputs;
        }

        /// <summary>
        ///     Marks the operation as completed successfully.
        /// </summary>
        public void Complete()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            logger.CompleteOperation(context, _outputs);
        }

        /// <summary>
        ///     Marks the operation as failed with error details.
        /// </summary>
        public void Fail(Exception error, ErrorType errorType, DependencyName? dependency = null)
        {
            Fail(error, errorType, _outputs, dependency);
        }

        /// <summary>
        ///     Marks the operation as failed with error details and outputs.
        /// </summary>
        public void Fail(Exception error, ErrorType errorType, Dictionary<string, object>? outputs, DependencyName? dependency = null)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _error = error;
            _errorType = errorType;
            _dependency = dependency;
            _outputs = outputs;
            logger.FailOperation(context, error, errorType, outputs, dependency);
        }
    }

    /// <summary>
    ///     No-op scope implementation for when chunked logging is disabled.
    ///     Provides IDisposable implementation that does nothing.
    /// </summary>
    private sealed class NoOpScope : IChunkedOperation
    {
        public OperationContext? Context => null;

        public void SetOutputs(Dictionary<string, object> outputs)
        {
            // No-op
        }

        public void Complete()
        {
            // No-op
        }

        public void Fail(Exception error, ErrorType errorType, DependencyName? dependency = null)
        {
            // No-op
        }

        public void Fail(Exception error, ErrorType errorType, Dictionary<string, object>? outputs, DependencyName? dependency = null)
        {
            // No-op
        }

        public void Dispose()
        {
            // No-op: chunked logging is disabled
        }
    }
}

/// <summary>
///     Generic version of ChunkedLogger that automatically uses the consumer's type name as category
///     and preserves the SourceContext of the consumer.
/// </summary>
/// <typeparam name="T">The type of the consumer (e.g., a background service or controller)</typeparam>
public sealed class ChunkedLogger<T>(ILogger<T> logger, ChunkedLoggerOptions? options = null)
    : ChunkedLogger(logger, typeof(T).Name, options)
{
}
