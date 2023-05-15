// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace Cli.Tests;

internal static class VerifyExtensions
{
    public static void UseParametersHash(this VerifySettings settings, params object?[] parameters)
    {
        StringBuilder paramsToHash = new();

        foreach (object? value in parameters)
        {
            string? s = value switch
            {
                null => "null",
                string[] a => string.Join(",", a),
                IEnumerable<object> e => string.Join(",", e.Select(x => x.ToString())),
                _ => value.ToString()
            };

            paramsToHash.Append(s);
        }

        using MD5 md5Hash = MD5.Create();
        byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(paramsToHash.ToString()));

        StringBuilder hashBuilder = new();

        for (int i = 0; i < data.Length; i++)
        {
            hashBuilder.Append(data[i].ToString("x2"));
        }

        settings.UseTextForParameters(hashBuilder.ToString());
    }
}
