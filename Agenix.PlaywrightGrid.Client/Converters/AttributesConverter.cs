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
using System.Text.Json.Serialization;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;

namespace Agenix.PlaywrightGrid.Client.Converters;

/// <summary>
/// Converts between backend string[] format and client ItemAttribute[] format for attributes.
/// Backend returns attributes as: ["key:value", "key2:value2", "singlekey"]
/// Client expects: [{ Key="key", Value="value" }, { Key="key2", Value="value2" }, { Key="singlekey", Value="" }]
/// </summary>
public class AttributesConverter : JsonConverter<IList<ItemAttribute>>
{
    public override IList<ItemAttribute>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array for attributes");

        var attributes = new List<ItemAttribute>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Backend format: "key:value" or "key"
                var attrString = reader.GetString();
                if (string.IsNullOrEmpty(attrString))
                    continue;

                var parts = attrString.Split(':', 2);
                attributes.Add(new ItemAttribute
                {
                    Key = parts[0],
                    Value = parts.Length > 1 ? parts[1] : ""
                });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Client format: { "key": "...", "value": "..." }
                ItemAttribute? attr = JsonSerializer.Deserialize<ItemAttribute>(ref reader, options);
                if (attr != null)
                    attributes.Add(attr);
            }
        }

        return attributes;
    }

    public override void Write(Utf8JsonWriter writer, IList<ItemAttribute> value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();

        foreach (var attr in value)
        {
            // Write as object: { "key": "...", "value": "..." }
            writer.WriteStartObject();
            writer.WriteString("key", attr.Key);
            writer.WriteString("value", attr.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
