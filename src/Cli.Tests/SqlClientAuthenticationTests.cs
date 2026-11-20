// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Microsoft.Data.SqlClient;

namespace Cli.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SqlClientAuthenticationTests
{
    private const string CLIENT_ID = "11111111-1111-1111-1111-111111111111";
    private readonly string? _originalClientId =
        Environment.GetEnvironmentVariable(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR);
    private readonly Dictionary<SqlAuthenticationMethod, SqlAuthenticationProvider?> _originalProviders =
        Enum.GetValues<SqlAuthenticationMethod>().ToDictionary(method => method, SqlAuthenticationProvider.GetProvider);

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR, _originalClientId);
        foreach ((SqlAuthenticationMethod method, SqlAuthenticationProvider? provider) in _originalProviders)
        {
            if (provider is not null)
            {
                Assert.IsTrue(SqlAuthenticationProvider.SetProvider(method, provider));
            }
        }
    }

    [TestMethod]
    public void RegisterProviders_ConfiguredClientId_RegistersSharedProviderForEverySupportedMethod()
    {
        Environment.SetEnvironmentVariable(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR, CLIENT_ID);

        SqlClientAuthentication.RegisterProviders();

        SqlAuthenticationProvider provider = SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryInteractive);
        Assert.IsInstanceOfType<ActiveDirectoryAuthenticationProvider>(provider);
        // SqlClient does not expose the constructor's client ID through its public API.
        FieldInfo? clientIdField = typeof(ActiveDirectoryAuthenticationProvider)
            .GetField("_applicationClientId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(clientIdField);
        Assert.AreEqual(CLIENT_ID, clientIdField.GetValue(provider));

        foreach (SqlAuthenticationMethod method in Enum.GetValues<SqlAuthenticationMethod>())
        {
            if (provider.IsSupported(method))
            {
                Assert.AreSame(provider, SqlAuthenticationProvider.GetProvider(method), method.ToString());
            }
            else
            {
                Assert.AreSame(_originalProviders[method], SqlAuthenticationProvider.GetProvider(method), method.ToString());
            }
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t")]
    public void RegisterProviders_NoClientId_PreservesExistingProviders(string? clientId)
    {
        Environment.SetEnvironmentVariable(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR, clientId);

        SqlClientAuthentication.RegisterProviders();

        foreach ((SqlAuthenticationMethod method, SqlAuthenticationProvider? provider) in _originalProviders)
        {
            Assert.AreSame(provider, SqlAuthenticationProvider.GetProvider(method), method.ToString());
        }
    }
}
