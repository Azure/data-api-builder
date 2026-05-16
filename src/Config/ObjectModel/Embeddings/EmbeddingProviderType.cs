// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Represents the supported embedding provider types.
/// </summary>
[JsonConverter(typeof(EnumMemberJsonEnumConverterFactory))]
public enum EmbeddingProviderType
{
    /// <summary>
    /// Azure OpenAI embedding provider.
    /// </summary>
    [EnumMember(Value = "azure-openai")]
    AzureOpenAI,

    /// <summary>
    /// OpenAI embedding provider.
    /// </summary>
    [EnumMember(Value = "openai")]
    OpenAI
}
