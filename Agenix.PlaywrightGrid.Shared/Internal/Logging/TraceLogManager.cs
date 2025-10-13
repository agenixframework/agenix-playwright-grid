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

namespace Agenix.PlaywrightGrid.Shared.Internal.Logging;

/// <summary>
///     Class to manage all internal loggers.
/// </summary>
public class TraceLogManager
{
    private static readonly Lazy<TraceLogManager> InstanceHolder = new(() => new TraceLogManager());

    private static readonly object _lockObj = new();

    private static Dictionary<Type, ITraceLogger> _traceLoggers;

    private string _baseDir = Environment.CurrentDirectory;

    static TraceLogManager()
    {
    }

    /// <summary>
    ///     Returns a single instance of <see cref="TraceLogManager" />
    /// </summary>
    public static TraceLogManager Instance => InstanceHolder.Value;


    /// <summary>
    ///     Fluently sets BaseDir.
    /// </summary>
    /// <param name="baseDir"></param>
    /// <returns></returns>
    public TraceLogManager WithBaseDir(string baseDir)
    {
        _baseDir = baseDir;

        return this;
    }

    /// <summary>
    ///     Gets or creates new logger for requested type.
    /// </summary>
    /// <param name="type">Type where logger should be registered for</param>
    /// <returns><see cref="ITraceLogger" /> instance for logging internal messages</returns>
    public ITraceLogger GetLogger(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_traceLoggers == null)
        {
            lock (_lockObj)
            {
                _traceLoggers ??= [];
            }
        }

        lock (_lockObj)
        {
            if (!_traceLoggers.ContainsKey(type))
            {
                var envTraceLevelValue = Environment.GetEnvironmentVariable("AgenixPlaywright_TraceLevel");

                SourceLevels traceLevel;

                if (string.IsNullOrEmpty(envTraceLevelValue))
                {
                    traceLevel = SourceLevels.Error;
                }
                else if (!Enum.TryParse(envTraceLevelValue, true, out traceLevel))
                {
                    throw new ArgumentOutOfRangeException(
                        $"Trace level '{envTraceLevelValue}' is not recognized. Known levels are [{string.Join(", ", Enum.GetNames(typeof(SourceLevels)))}]");
                }

                var traceSource = new TraceSource(type.Name)
                {
                    Switch = new SourceSwitch("AgenixPlaywright_TraceSwitch", traceLevel.ToString())
                };

                var logFileName = $"{type.Assembly.GetName().Name}.{Environment.ProcessId}.log";

                logFileName = Path.Combine(_baseDir, logFileName);

                var traceListener = new DefaultTraceListener
                {
                    Filter = new SourceFilter(traceSource.Name),
                    LogFileName = logFileName
                };

                traceSource.Listeners.Add(traceListener);

                _traceLoggers[type] = new TraceLogger(traceSource);
            }
        }

        return _traceLoggers[type];
    }

    /// <summary>
    ///     Gets or creates new logger for requested type.
    /// </summary>
    /// <typeparam name="T">Type where logger should be registered for</typeparam>
    /// <returns><see cref="ITraceLogger" /> instance for logging internal messages</returns>
    public ITraceLogger GetLogger<T>()
    {
        return GetLogger(typeof(T));
    }
}
