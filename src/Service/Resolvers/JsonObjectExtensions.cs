// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// This extension class provides helpers to convert a mutable JSON object
/// to a JSON element or JSON document.
/// </summary>
internal static class JsonObjectExtensions
{
    /// <summary>
    /// Converts a mutable JSON object to an immutable JSON element.
    /// </summary>
    /// <param name="obj">
    /// The mutable JSON object to convert.
    /// </param>
    /// <returns>
    /// An immutable JSON element.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="obj"/> is <see langword="null"/>.
    /// </exception>
    public static JsonElement ToJsonElement(this JsonObject obj)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        // we first write the mutable JsonObject to the pooled buffer and avoid serializing
        // to a full JSON string.
        using ArrayPoolWriter buffer = new();
        obj.WriteTo(buffer);

        // next we take the reader here and parse the JSON element from the buffer.
        Utf8JsonReader reader = new(buffer.GetWrittenSpan());
            
        // the underlying JsonDocument will not use pooled arrays to store metadata on it ...
        // this JSON element can be safely returned.
        return JsonElement.ParseValue(ref reader);
    }
    
    /// <summary>
    /// Converts a mutable JSON object to an immutable JSON document.
    /// </summary>
    /// <param name="obj">
    /// The mutable JSON object to convert.
    /// </param>
    /// <returns>
    /// An immutable JSON document.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="obj"/> is <see langword="null"/>.
    /// </exception>
    public static JsonDocument ToJsonDocument(this JsonObject obj)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        // we first write the mutable JsonObject to the pooled buffer and avoid serializing
        // to a full JSON string.
        using ArrayPoolWriter buffer = new();
        obj.WriteTo(buffer);

        // next we parse the JSON document from the buffer.
        // this JSON document will be disposed by the GraphQL execution engine.
        return JsonDocument.Parse(buffer.GetWrittenMemory());
    }
    
    private static void WriteTo(this JsonObject obj, IBufferWriter<byte> bufferWriter)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (bufferWriter == null)
        {
            throw new ArgumentNullException(nameof(bufferWriter));
        }
        
        using Utf8JsonWriter writer = new(bufferWriter);
        obj.WriteTo(writer);
        writer.Flush();
    }
}
