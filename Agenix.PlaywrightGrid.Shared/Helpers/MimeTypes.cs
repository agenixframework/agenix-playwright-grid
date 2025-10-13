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

namespace Agenix.PlaywrightGrid.Shared.Helpers;

/// <summary>
///     MIME type mapping utility.
/// </summary>
public static class MimeTypes
{
    /// <summary>
    ///     Provides MIME type mapping for common file extensions.
    /// </summary>
    public static class MimeTypeMap
    {
        private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".bmp", "image/bmp" },
            { ".svg", "image/svg+xml" },
            { ".webp", "image/webp" },
            { ".ico", "image/x-icon" },

            // Documents
            { ".pdf", "application/pdf" },
            { ".doc", "application/msword" },
            { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { ".xls", "application/vnd.ms-excel" },
            { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { ".ppt", "application/vnd.ms-powerpoint" },
            { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },

            // Text
            { ".txt", "text/plain" },
            { ".html", "text/html" },
            { ".htm", "text/html" },
            { ".css", "text/css" },
            { ".csv", "text/csv" },
            { ".xml", "text/xml" },
            { ".json", "application/json" },

            // Video
            { ".mp4", "video/mp4" },
            { ".webm", "video/webm" },
            { ".avi", "video/x-msvideo" },
            { ".mov", "video/quicktime" },
            { ".wmv", "video/x-ms-wmv" },

            // Audio
            { ".mp3", "audio/mpeg" },
            { ".wav", "audio/wav" },
            { ".ogg", "audio/ogg" },

            // Archives
            { ".zip", "application/zip" },
            { ".tar", "application/x-tar" },
            { ".gz", "application/gzip" },
            { ".7z", "application/x-7z-compressed" },
            { ".rar", "application/x-rar-compressed" }
        };

        /// <summary>
        ///     Gets the MIME type for the specified file extension.
        /// </summary>
        /// <param name="fileExtension">File extension (with or without leading dot)</param>
        /// <returns>MIME type string</returns>
        /// <exception cref="ArgumentNullException">Thrown when fileExtension is null</exception>
        public static string GetMimeType(string fileExtension)
        {
            ArgumentNullException.ThrowIfNull(fileExtension);

            // Normalize extension (add leading dot if missing)
            var normalized = fileExtension.StartsWith(".")
                ? fileExtension
                : $".{fileExtension}";

            // Return mapped MIME type or default to octet-stream
            return Mappings.TryGetValue(normalized, out var mimeType)
                ? mimeType
                : "application/octet-stream";
        }
    }
}
