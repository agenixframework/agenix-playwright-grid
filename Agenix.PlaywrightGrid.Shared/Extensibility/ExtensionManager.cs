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

using System.Reflection;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;

namespace Agenix.PlaywrightGrid.Shared.Extensibility;

/// <summary>
///     Represents an extension manager for managing extensions.
/// </summary>
public class ExtensionManager : IExtensionManager
{
    private static readonly ITraceLogger _traceLogger = TraceLogManager.Instance.GetLogger(typeof(ExtensionManager));

    private static readonly Lazy<IExtensionManager> _instance = new(() =>
    {
        var ext = new ExtensionManager();

        var extentionDirectories = new List<string>
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(ExtensionManager).Assembly.Location),
            Environment.CurrentDirectory
        };

        foreach (var extentionDirectory in extentionDirectories)
        {
            ext.Explore(extentionDirectory);
        }

        return ext;
    });

    private static readonly object _lockObj = new();

    private readonly List<string> _exploredAssemblyNames = [];

    private readonly List<string> _exploredPaths = [];

    /// <summary>
    ///     Gets the instance of the extension manager.
    /// </summary>
    public static IExtensionManager Instance => _instance.Value;

    /// <summary>
    ///     Explores the specified path for extensions.
    /// </summary>
    /// <param name="path">The path to explore.</param>
    public void Explore(string path)
    {
        if (!_exploredPaths.Contains(path))
        {
            lock (_lockObj)
            {
                if (!_exploredPaths.Contains(path))
                {
                    _traceLogger.Info($"Exploring extensions in '{path}' directory.");

                    var reportEventObservers = new List<IReportEventsObserver>();
                    var commandsListeners = new List<ICommandsListener>();

                    var currentDirectory = new DirectoryInfo(path);

                    foreach (var file in currentDirectory.GetFiles("*ReportPortal*.dll"))
                    {
                        _traceLogger.Verbose($"Found '{file.Name}' and loading it into current AppDomain.");
                        try
                        {
                            AppDomain.CurrentDomain.Load(Path.GetFileNameWithoutExtension(file.Name));
                        }
                        catch (Exception ex)
                        {
                            _traceLogger.Warn($"Could not load extension assembly into application domain. {ex}");
                        }
                    }

                    var iReportEventObserseExtensionInterfaceType = typeof(IReportEventsObserver);
                    var iCommandsListenerInterfaceType = typeof(ICommandsListener);

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                                 .Where(a => a.GetName().Name.Contains("ReportPortal")))
                    {
                        if (!_exploredAssemblyNames.Contains(assembly.FullName))
                        {
                            _exploredAssemblyNames.Add(assembly.FullName);
                            _traceLogger.Verbose($"Exploring '{assembly.FullName}' assembly for extensions.");

                            try
                            {
                                foreach (var type in assembly.GetTypes().Where(t => t.IsClass))
                                {
                                    if (!type.IsAbstract && type.GetConstructors()
                                            .Any(ctor => ctor.GetParameters().Length == 0))
                                    {
                                        if (iReportEventObserseExtensionInterfaceType.IsAssignableFrom(type))
                                        {
                                            var extension = Activator.CreateInstance(type);
                                            reportEventObservers.Add((IReportEventsObserver)extension);
                                            _traceLogger.Info(
                                                $"Registered '{type.FullName}' type as {nameof(IReportEventsObserver)} extension.");
                                        }

                                        if (iCommandsListenerInterfaceType.IsAssignableFrom(type))
                                        {
                                            var extension = Activator.CreateInstance(type);
                                            commandsListeners.Add((ICommandsListener)extension);
                                            _traceLogger.Info(
                                                $"Registered '{type.FullName}' type as {nameof(ICommandsListener)} extension.");
                                        }
                                    }
                                }
                            }
                            catch (ReflectionTypeLoadException exp)
                            {
                                _traceLogger.Warn(
                                    $"Couldn't load '{assembly.GetName().Name}' assembly into domain.\n{exp}");
                                foreach (var loaderException in exp.LoaderExceptions)
                                {
                                    _traceLogger.Warn(loaderException.ToString());
                                }
                            }
                        }
                    }

                    reportEventObservers.ForEach(reo => ReportEventObservers.Add(reo));
                    commandsListeners.ForEach(cl => CommandsListeners.Add(cl));

                    _exploredPaths.Add(path);
                }
            }
        }
        else
        {
            _traceLogger.Verbose($"The extensions '{path}' path was visited before, skipping");
        }
    }

    /// <summary>
    ///     Gets the list of report event observers.
    /// </summary>
    public IList<IReportEventsObserver> ReportEventObservers { get; } = [];

    /// <summary>
    ///     Gets the list of commands listeners.
    /// </summary>
    public IList<ICommandsListener> CommandsListeners { get; } = [];
}
