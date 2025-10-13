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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using Agenix.PlaywrightGrid.Shared.MimeTypes;
using Agenix.PlaywrightGrid.Shared.Reporter;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Embedded.LaunchArtifacts;

public class LaunchArtifactsEventsObserver : IReportEventsObserver
{
    private static readonly ITraceLogger _logger =
        TraceLogManager.Instance.GetLogger(typeof(LaunchArtifactsEventsObserver));

    public string BaseDirectory { get; set; } = Environment.CurrentDirectory;

    public void Initialize(IReportEventsSource reportEventsSource)
    {
        reportEventsSource.OnBeforeLaunchFinishing += ReportEventsSource_OnBeforeLaunchFinishing;
    }

    private void ReportEventsSource_OnBeforeLaunchFinishing(ILaunchReporter launchReporter,
        BeforeLaunchFinishingEventArgs args)
    {
        var artifactPaths = args.Configuration.GetValues<string>("Launch:Artifacts", null);

        if (artifactPaths != null)
        {
            foreach (var filePattern in artifactPaths)
            {
                var artifacts = Directory.GetFiles(BaseDirectory, filePattern.Trim());

                foreach (var artifact in artifacts)
                {
                    var createLogItemRequest = new CreateLogItemRequest
                    {
                        LaunchUuid = launchReporter.Info.Uuid,
                        Time = DateTime.UtcNow,
                        Level = "TRACE",
                        Text = Path.GetFileName(artifact)
                    };

                    AttachFile(artifact, ref createLogItemRequest);

                    Task.Run(async () => await args.ClientService.LogItem.CreateAsync(createLogItemRequest))
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    private static void AttachFile(string filePath, ref CreateLogItemRequest request)
    {
        try
        {
            using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();

            request.Attach = new LogItemAttach
            {
                Name = Path.GetFileName(filePath),
                MimeType = MimeTypeMap.GetMimeType(Path.GetExtension(filePath)),
                DataBase64 = Convert.ToBase64String(bytes)
            };
        }
        catch (Exception ex)
        {
            request.Text = $"{request.Text}\n> Couldn't read content of `{filePath}` file. \n{ex}";
        }
    }
}
