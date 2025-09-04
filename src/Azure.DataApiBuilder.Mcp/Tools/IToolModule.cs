// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Tools;

/// <summary>
/// Interface for modular tool registration.
/// Implement this interface to create new tool modules that can be dynamically registered.
/// </summary>
public interface IToolModule
{
    /// <summary>
    /// Registers the tools provided by this module with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register tools with</param>
    void RegisterTools(IServiceCollection services);

    /// <summary>
    /// Gets the name of this tool module for logging and identification purposes.
    /// </summary>
    string ModuleName { get; }
}
