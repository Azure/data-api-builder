// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;

namespace Azure.DataApiBuilder.Config.Utilities;

public static class HttpStatusCodeExtensions
{
    /// <summary>
    /// Check for status code within 4xx range
    /// </summary>
    /// <param name="statusCode"></param>
    /// <returns></returns>
    public static bool IsClientError(this HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return code >= 400 && code < 500;
    }
}
