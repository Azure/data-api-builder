// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: IConfigValidator.cs
// **************************************

namespace Azure.DataApiBuilder.Service.Configurations
{

    /// <summary>
    /// Validates the runtime config.
    /// </summary>
    public interface IConfigValidator
    {
        /// <summary>
        /// Validate the runtime config both within the
        /// config itself and in relation to the schema if available.
        /// </summary>
        void ValidateConfig();
    }
}
