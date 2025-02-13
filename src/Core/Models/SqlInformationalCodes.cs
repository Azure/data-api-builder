// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    public enum SqlInformationalCodes : int
    {

        /// <summary>
        /// MsSQL information code that contains the statement ID, query hash and
        /// distributed request ID. This information can be used for development purposes.
        /// </summary>
        MSSQL_STATEMENT_ID_INFORMATION_CODE = 15806,

        /// <summary>
        /// Prints statement ID for supportability purposes. If a non-distributed query hits an issue, it'd be helpful if the customer could share the statement id.
        /// </summary>
        /// <remarks>
        /// Text in summary is copied verbatim from the engine definition.
        /// </remarks>
        DW_FABRIC_QUERY_IDENTIFIER = 24528,

    }
}
