// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service;

public static class JwtHttpClientFactory
{
    public static HttpClient Create()
    {
        bool allowSelfSigned = Environment.GetEnvironmentVariable("USE_SELF_SIGNED_CERT")
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        HttpClientHandler handler = new();

        if (allowSelfSigned)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler, disposeHandler: true);
    }
}
