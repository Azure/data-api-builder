// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    public enum SqlInformationalCodes : int
    {

        /// <summary>
        /// Prints statement ID, query hash and distributed request ID for development purposes.
        /// </summary>
        /// <remarks>
        /// Text in summary is copied verbatim from the engine definition.
        /// </remarks>
        FABRIC_QUERY_EXECUTOR_REQUEST_IDENTIFIER = 15806,

        /// <summary>
        /// Prints statement ID for supportability purposes. If a non-distributed query hits an issue, it'd be helpful if the customer could share the statement id.
        /// </summary>
        /// <remarks>
        /// Text in summary is copied verbatim from the engine definition.
        /// </remarks>
        DW_FABRIC_QUERY_IDENTIFIER = 24528,

    }
}
