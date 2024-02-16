// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class DatabaseObjectConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(DatabaseObject).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JObject jsonObject = JObject.Load(reader);
        string typeName = jsonObject["TypeName"]!.ToObject<string>() ?? throw new JsonSerializationException("TypeName is missing");
        Type concreteType = GetTypeFromName(typeName);

        if (concreteType == null)
        {
            throw new JsonSerializationException($"Unknown type: {typeName}");
        }

        DatabaseObject objA = (DatabaseObject)Activator.CreateInstance(concreteType)!;
        serializer.Populate(jsonObject.CreateReader(), objA);

        return objA;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        DatabaseObject obj = (DatabaseObject)value!;
        JObject jsonObject = new JObject()!;

        // Add TypeName
        jsonObject.Add("TypeName", obj.GetType().AssemblyQualifiedName);

        // Add other properties of DatabaseObject
        foreach (PropertyInfo prop in obj.GetType().GetProperties())
        {
            // Skip the TypeName property, as it has been handled above
            if (prop.Name == "TypeName")
            {
                continue;
            }

            jsonObject.Add(prop.Name, JToken.FromObject(prop.GetValue(obj)!));
        }

        jsonObject.WriteTo(writer);
    }

    private static Type GetTypeFromName(string typeName)
    {
        Type type = Type.GetType(typeName)!;

        if (type == null)
        {
            throw new JsonSerializationException($"Could not find type: {typeName}");
        }

        return type;
    }
}

