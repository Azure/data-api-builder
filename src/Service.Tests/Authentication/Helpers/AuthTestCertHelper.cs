// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    internal class AuthTestCertHelper
    {
        /// <summary>
        /// Creates self signed cert for local unit/integration testing where connecting to internet
        /// is not viable (such as connecting to an OpenID Connect Well Known Config endpoint). This is
        /// to simulate JWTs with kid claim, which is optional per https://www.rfc-editor.org/rfc/rfc7515.html#section-4.1.4
        /// - Usage of RSA.Create() https://stackoverflow.com/a/42006084/18174950
        /// - Usage of SHA256: Recommended for JWTs per https://datatracker.ietf.org/doc/html/rfc7518#section-3.1
        /// - Usage of RSASignaturePadding.Pss: https://docs.microsoft.com/dotnet/api/system.security.cryptography.rsasignaturepaddingmode#system-security-cryptography-rsasignaturepaddingmode-pss
        /// </summary>
        /// <param name="hostName"></param>
        /// <returns></returns>
        public static X509Certificate2 CreateSelfSignedCert(string hostName)
        {
            CertificateRequest request = new($"CN={hostName}", RSA.Create(keySizeInBits: 2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            return request.CreateSelfSigned(notBefore: DateTime.UtcNow, notAfter: DateTime.UtcNow.AddMinutes(5));
        }
    }
}
