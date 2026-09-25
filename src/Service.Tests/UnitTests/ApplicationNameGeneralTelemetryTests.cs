// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>Offline tests of the six General flags and their positional/connection-string contracts.</summary>
[TestClass]
[DoNotParallelize]
public class ApplicationNameGeneralTelemetryTests
{
    private const string SQL_MI = "Server=unit-test.invalid;Authentication=Active Directory Managed Identity;";
    private const string SQL_PASSWORD = "Server=unit-test.invalid;User ID=test-user;Password=test-only;";
    private static readonly ApplicationNameTelemetryEnvironment _environment = new('L', '1', 'A', 'C');
    private string? _originalOptOut;
    private string? _originalAppName;

    /// <summary>Embedding tests must not inherit an ambient opt-out or custom marker.</summary>
    [TestInitialize]
    public void SaveEnvironment()
    {
        _originalOptOut = Environment.GetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR);
        _originalAppName = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);
        Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, null);
        Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
    }

    /// <summary>Preserves environment settings supplied by the test runner or developer.</summary>
    [TestCleanup]
    public void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, _originalOptOut);
        Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, _originalAppName);
    }

    /// <summary>All six fields are in the specified order without shifting the other sections.</summary>
    [TestMethod]
    public void EncodeGeneral_HasExpectedPositions()
    {
        DataSource source = new(DatabaseType.MSSQL, SQL_MI);
        string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(Config(source), source, _environment);
        string[] sections = Sections(telemetry);

        Assert.AreEqual("XXSX", sections[0]);
        Assert.AreEqual("L1AC01", sections[1]);
        Assert.AreEqual(20, sections[2].Length);
        Assert.AreEqual(14, sections[3].Length);
        Assert.IsTrue(telemetry.Length <= 128, "The plain token must fit the SQL Server application-name budget.");
    }

    /// <summary>OS detection uses the runtime platform, not the mutable OS environment variable.</summary>
    [TestMethod]
    public void CaptureOperatingSystem_UsesRuntimePlatform()
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new() { ["OS"] = "untrusted-host-value" });
        char expected = OperatingSystem.IsWindows() ? 'W' : OperatingSystem.IsLinux() ? 'L'
            : OperatingSystem.IsMacOS() ? 'M' : 'O';

        Assert.AreEqual(expected, snapshot.OperatingSystem);
    }

    /// <summary>Both .NET container flags support booleans and 0/1 without assuming absent means false.</summary>
    [DataTestMethod]
    [DataRow(null, null, 'M')]
    [DataRow("", " ", 'M')]
    [DataRow("true", null, '1')]
    [DataRow(null, "TRUE", '1')]
    [DataRow("1", null, '1')]
    [DataRow(null, "1", '1')]
    [DataRow("false", null, '0')]
    [DataRow(null, "False", '0')]
    [DataRow("0", null, '0')]
    [DataRow(null, "0", '0')]
    [DataRow(" true ", "1", '1')]
    [DataRow("false", "0", '0')]
    [DataRow("yes", "invalid", 'M')]
    [DataRow("invalid", "true", '1')]
    [DataRow("false", "true", 'M')]
    [DataRow("1", "0", 'M')]
    public void CaptureContainer_EncodesKnownOrMissing(string singular, string plural, char expected)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = singular,
            ["DOTNET_RUNNING_IN_CONTAINERS"] = plural,
        });

        Assert.AreEqual(expected, snapshot.RunningInContainer);
    }

    /// <summary>Only runtime hosting signals identify a cloud; ordinary SDK configuration does not.</summary>
    [DataTestMethod]
    [DataRow("CONTAINER_APP_NAME", "example", 'A', 'C')]
    [DataRow("CONTAINER_APP_REVISION", "example", 'A', 'C')]
    [DataRow("CONTAINER_APP_JOB_NAME", "example", 'A', 'C')]
    [DataRow("CONTAINER_APP_JOB_EXECUTION_NAME", "example", 'A', 'C')]
    [DataRow("WEBSITE_SITE_NAME", "example", 'A', 'S')]
    [DataRow("WEBSITE_INSTANCE_ID", "example", 'A', 'S')]
    [DataRow("AWS_EXECUTION_ENV", "example", 'W', 'N')]
    [DataRow("AWS_LAMBDA_FUNCTION_NAME", "example", 'W', 'N')]
    [DataRow("ECS_CONTAINER_METADATA_URI", "example", 'W', 'N')]
    [DataRow("ECS_CONTAINER_METADATA_URI_V4", "example", 'W', 'N')]
    [DataRow("K_SERVICE", "example", 'O', 'N')]
    [DataRow("CLOUD_RUN_JOB", "example", 'G', 'N')]
    [DataRow("CLOUD_RUN_WORKER_POOL", "example", 'G', 'N')]
    [DataRow("GAE_ENV", "standard", 'G', 'N')]
    [DataRow("KUBERNETES_SERVICE_HOST", "example", 'O', 'N')]
    [DataRow("WEBSITE_SITE_NAME", " ", 'L', 'N')]
    [DataRow("AZURE_CLIENT_ID", "example", 'L', 'N')]
    [DataRow("AZURE_TENANT_ID", "example", 'L', 'N')]
    [DataRow("AZURE_FEDERATED_TOKEN_FILE", "example", 'L', 'N')]
    [DataRow("IDENTITY_ENDPOINT", "example", 'L', 'N')]
    [DataRow("AWS_REGION", "example", 'L', 'N')]
    [DataRow("GOOGLE_CLOUD_PROJECT", "example", 'L', 'N')]
    [DataRow("DAB_APP_NAME_ENV", "dab_hosted", 'L', 'N')]
    public void CaptureHosting_UsesRuntimeSignals(string name, string value, char host, char service)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new() { [name] = value });

        Assert.AreEqual(host, snapshot.HostingEnvironment);
        Assert.AreEqual(service, snapshot.AzureHostingService);
    }

    /// <summary>Absent cloud signals use the agreed Local/Not Azure best-effort fallback.</summary>
    [TestMethod]
    public void CaptureHosting_AbsentSignalsAreLocal()
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new());

        Assert.AreEqual('M', snapshot.RunningInContainer);
        Assert.AreEqual('L', snapshot.HostingEnvironment);
        Assert.AreEqual('N', snapshot.AzureHostingService);
    }

    /// <summary>Conflicting automatic signals are not arbitrarily assigned to one cloud or Azure service.</summary>
    [TestMethod]
    public void CaptureHosting_ConflictingSignalsAreMissing()
    {
        ApplicationNameTelemetryEnvironment clouds = Capture(new()
        {
            ["WEBSITE_SITE_NAME"] = "example",
            ["AWS_EXECUTION_ENV"] = "example",
        });
        Assert.AreEqual('M', clouds.HostingEnvironment);
        Assert.AreEqual('M', clouds.AzureHostingService);

        ApplicationNameTelemetryEnvironment azureServices = Capture(new()
        {
            ["WEBSITE_SITE_NAME"] = "example",
            ["CONTAINER_APP_NAME"] = "example",
        });
        Assert.AreEqual('A', azureServices.HostingEnvironment);
        Assert.AreEqual('M', azureServices.AzureHostingService);
    }

    /// <summary>Portable Knative variables are not evidence that a Kubernetes cluster is on GCP.</summary>
    [TestMethod]
    public void Review_KnativeIsNotProofOfGcp()
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            ["K_SERVICE"] = "example",
            ["KUBERNETES_SERVICE_HOST"] = "example",
        });

        Assert.AreEqual('O', snapshot.HostingEnvironment);
        Assert.AreEqual('N', snapshot.AzureHostingService);
    }

    /// <summary>Automatic cloud inference must not replace an explicit missing/not-Azure service.</summary>
    [DataTestMethod]
    [DataRow("M", false, 'W', 'M')]
    [DataRow("N", true, 'M', 'N')]
    public void Review_ServiceOverrideWinsOverAutomaticHosting(string service, bool conflictingClouds, char expectedHost, char expectedService)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            [ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR] = service,
            ["AWS_EXECUTION_ENV"] = "example",
            ["CLOUD_RUN_JOB"] = conflictingClouds ? "example" : null,
        });

        Assert.AreEqual(expectedHost, snapshot.HostingEnvironment);
        Assert.AreEqual(expectedService, snapshot.AzureHostingService);
    }

    /// <summary>Not Azure suppresses Azure detection but still permits a different cloud signal.</summary>
    [DataTestMethod]
    [DataRow(false, 'L')]
    [DataRow(true, 'W')]
    public void CaptureAzureService_NotAzureOverrideWinsOverDetection(bool aws, char expectedHost)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            [ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR] = "NotAzure",
            ["WEBSITE_SITE_NAME"] = "stale-azure-hint",
            ["AWS_EXECUTION_ENV"] = aws ? "example" : null,
        });

        Assert.AreEqual(expectedHost, snapshot.HostingEnvironment);
        Assert.AreEqual('N', snapshot.AzureHostingService);
    }

    /// <summary>Explicit hosting overrides win over automatic Azure signals and accept names or codes.</summary>
    [DataTestMethod]
    [DataRow("L", 'L', 'N')]
    [DataRow(" local ", 'L', 'N')]
    [DataRow("A", 'A', 'S')]
    [DataRow("Azure", 'A', 'S')]
    [DataRow("W", 'W', 'N')]
    [DataRow("aws", 'W', 'N')]
    [DataRow("G", 'G', 'N')]
    [DataRow("GCP", 'G', 'N')]
    [DataRow("O", 'O', 'N')]
    [DataRow("Other", 'O', 'N')]
    [DataRow("M", 'M', 'M')]
    [DataRow("Missing", 'M', 'M')]
    [DataRow("invalid", 'M', 'M')]
    [DataRow(" ", 'A', 'S')]
    public void CaptureHosting_OverrideTakesPrecedence(string value, char host, char service)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            [ApplicationNameTelemetry.HOSTING_ENVIRONMENT_ENV_VAR] = value,
            ["WEBSITE_SITE_NAME"] = "example",
        });

        Assert.AreEqual(host, snapshot.HostingEnvironment);
        Assert.AreEqual(service, snapshot.AzureHostingService);
    }

    /// <summary>A service override supports services without universal runtime environment markers.</summary>
    [DataTestMethod]
    [DataRow("C", 'C')]
    [DataRow("ContainerApps", 'C')]
    [DataRow(" container apps ", 'C')]
    [DataRow("K", 'K')]
    [DataRow("aks", 'K')]
    [DataRow("S", 'S')]
    [DataRow("AppService", 'S')]
    [DataRow("App Service", 'S')]
    [DataRow("I", 'I')]
    [DataRow("ACI", 'I')]
    [DataRow("ContainerInstances", 'I')]
    [DataRow("Container Instances", 'I')]
    [DataRow("O", 'O')]
    [DataRow("Other", 'O')]
    public void CaptureAzureService_OverrideAlsoIdentifiesAzure(string value, char service)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            [ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR] = value,
        });

        Assert.AreEqual('A', snapshot.HostingEnvironment);
        Assert.AreEqual(service, snapshot.AzureHostingService);
    }

    /// <summary>Overrides remain internally consistent, with hosting taking precedence over service.</summary>
    [DataTestMethod]
    [DataRow("AWS", "AKS", 'W', 'N')]
    [DataRow("Local", "ContainerApps", 'L', 'N')]
    [DataRow("Missing", "AKS", 'M', 'M')]
    [DataRow("Azure", "N", 'A', 'M')]
    [DataRow("Azure", "NotAzure", 'A', 'M')]
    [DataRow("Azure", "Not Azure", 'A', 'M')]
    [DataRow("Azure", "M", 'A', 'M')]
    [DataRow("Azure", "Missing", 'A', 'M')]
    [DataRow("Azure", "invalid", 'A', 'M')]
    [DataRow("Azure", null, 'A', 'M')]
    [DataRow(null, "NotAzure", 'L', 'N')]
    [DataRow(null, "Not Azure", 'L', 'N')]
    public void CaptureHosting_OverrideCombinations(string hostOverride, string serviceOverride, char host, char service)
    {
        ApplicationNameTelemetryEnvironment snapshot = Capture(new()
        {
            [ApplicationNameTelemetry.HOSTING_ENVIRONMENT_ENV_VAR] = hostOverride,
            [ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR] = serviceOverride,
        });

        Assert.AreEqual(host, snapshot.HostingEnvironment);
        Assert.AreEqual(service, snapshot.AzureHostingService);
    }

    /// <summary>Public encoding rereads explicit overrides rather than keeping a process-wide snapshot.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void EncodeGeneral_RereadsEnvironment()
    {
        string variable = ApplicationNameTelemetry.HOSTING_ENVIRONMENT_ENV_VAR;
        string original = Environment.GetEnvironmentVariable(variable);
        try
        {
            RuntimeConfig config = Config(new(DatabaseType.MSSQL, SQL_PASSWORD));
            Environment.SetEnvironmentVariable(variable, "Local");
            Assert.AreEqual('L', Sections(ApplicationNameTelemetry.EncodeTelemetryString(config))[1][2]);
            Environment.SetEnvironmentVariable(variable, "AWS");
            Assert.AreEqual('W', Sections(ApplicationNameTelemetry.EncodeTelemetryString(config))[1][2]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    /// <summary>Data-source count includes loaded children and excludes nonexistent file references.</summary>
    [DataTestMethod]
    [DataRow(false, 0, 'M')]
    [DataRow(true, 0, '0')]
    [DataRow(false, 1, '0')]
    [DataRow(true, 1, '1')]
    [DataRow(false, 2, '1')]
    public void EncodeMultipleDataSources_CountsParsedSources(bool defaultSource, int childCount, char expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            DataSource source = new(DatabaseType.MSSQL, SQL_MI);
            List<string> files = new() { Path.Combine(directory, "missing.json") };
            for (int index = 0; index < childCount; index++)
            {
                string path = Path.Combine(directory, $"child-{index}.json");
                File.WriteAllText(path, Config(source).ToJson());
                files.Add(path);
            }

            RuntimeConfig config = new(Schema: "test", DataSource: defaultSource ? source : null,
                Entities: new(new Dictionary<string, Entity>()), DataSourceFiles: new(files));
            Assert.AreEqual(childCount + (defaultSource ? 1 : 0), config.ListAllDataSources().Count());
            Assert.AreEqual(expected, General(config)[4]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>MI is a configured authentication fact; unspecified/default credentials are unknown.</summary>
    [DataTestMethod]
    [DataRow(DatabaseType.MSSQL, SQL_MI, '1')]
    [DataRow(DatabaseType.DWSQL, SQL_MI, '1')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory MSI;", '1')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Managed Identity;User ID=client-id;", '1')]
    [DataRow(DatabaseType.MSSQL, SQL_PASSWORD, '0')]
    [DataRow(DatabaseType.DWSQL, SQL_PASSWORD, '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Integrated Security=true;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;User ID=test-user;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Sql Password;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Password;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Service Principal;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Integrated;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Interactive;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Device Code Flow;", '0')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Default;", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=Active Directory Workload Identity;", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;", 'M')]
    [DataRow(DatabaseType.MSSQL, "", 'M')]
    [DataRow(DatabaseType.MSSQL, " ", 'M')]
    [DataRow(DatabaseType.MSSQL, "not a connection string", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Authentication=unsupported;", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Connect Timeout=invalid;", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Connect Timeout=99999999999999999999;", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;Integrated Security=invalid;", 'M')]
    [DataRow(DatabaseType.MSSQL, "@env('UNRESOLVED_CONNECTION')", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;User ID=user;Password=@akv('unresolved');", 'M')]
    [DataRow(DatabaseType.MSSQL, "Server=test;User ID=@env('unresolved');", 'M')]
    [DataRow(DatabaseType.PostgreSQL, "Host=test;Username=user;Password=test-only;", '0')]
    [DataRow(DatabaseType.PostgreSQL, "Host=test;Username=user;", 'M')]
    [DataRow(DatabaseType.PostgreSQL, "not a connection string", 'M')]
    [DataRow(DatabaseType.PostgreSQL, "Host=test;Timeout=invalid;", 'M')]
    [DataRow(DatabaseType.PostgreSQL, "Host=test;Password=@akv('unresolved');", 'M')]
    [DataRow(DatabaseType.MySQL, "Server=test;User ID=user;Pwd=test-only;", '0')]
    [DataRow(DatabaseType.MySQL, "Server=test;User ID=user;Password=test-only;", '0')]
    [DataRow(DatabaseType.MySQL, "Server=test;User ID=user;", 'M')]
    [DataRow(DatabaseType.MySQL, "not a connection string", 'M')]
    [DataRow(DatabaseType.MySQL, "Server=test;Pwd=@env('unresolved');", 'M')]
    [DataRow(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://unit-test.invalid;AccountKey=test-only;", '0')]
    [DataRow(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://unit-test.invalid;", 'M')]
    [DataRow(DatabaseType.CosmosDB_NoSQL, "not a connection string", 'M')]
    [DataRow(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://unit-test.invalid;AccountKey=@akv('unresolved');", 'M')]
    [DataRow(DatabaseType.CosmosDB_PostgreSQL, "Server=test;", 'M')]
    public void EncodeManagedIdentity_UsesConfigurationOnly(DatabaseType type, string connectionString, char expected)
    {
        DataSource source = new(type, connectionString);
        Assert.AreEqual(expected, General(Config(source), source)[5]);
    }

    /// <summary>Per-pool MI follows the live source; only CLI/no-live-source encoding uses the default.</summary>
    [TestMethod]
    public void EncodeManagedIdentity_UsesLiveSourceOrDefault()
    {
        DataSource source = new(DatabaseType.MSSQL, SQL_MI);
        DataSource live = new(DatabaseType.PostgreSQL, "Host=test;Password=test-only;");
        RuntimeConfig config = Config(source);

        Assert.AreEqual('1', General(config)[5]);
        Assert.AreEqual('0', General(config, live)[5]);
        Assert.AreEqual('M', General(Config(null))[5]);
        Assert.AreEqual('1', General(Config(null), source)[5]);
    }

    /// <summary>OBO request pools and metadata MI pools share one source token, so MI is inconclusive.</summary>
    [TestMethod]
    public void EncodeManagedIdentity_OboWithMiIsMixed()
    {
        DataSource source = new(DatabaseType.MSSQL, SQL_MI)
        {
            UserDelegatedAuth = new(Enabled: true),
        };
        Assert.AreEqual('M', General(Config(source), source)[5]);
    }

    /// <summary>One token covers both OBO requests and metadata; do not claim a mixed/unknown mode is non-MI.</summary>
    [DataTestMethod]
    [DataRow(SQL_MI, 'M')]
    [DataRow("Server=unit-test.invalid;", 'M')]
    [DataRow(SQL_PASSWORD, '0')]
    public void Review_OboMetadataAuthenticationIsNotMisclassified(string connectionString, char expected)
    {
        DataSource source = new(DatabaseType.MSSQL, connectionString) { UserDelegatedAuth = new(Enabled: true) };

        Assert.AreEqual(expected, General(Config(source), source)[5]);
    }

    /// <summary>Alias handling must agree with the actual provider's effective password.</summary>
    [DataTestMethod]
    [DataRow("Server=test;User ID=user;Password=old;Pwd=;")]
    [DataRow("Server=test;User ID=user;Pwd=old;Password=;")]
    [DataRow("Server=test;User ID=user;Password=old;Pwd=;Password=new;")]
    [DataRow("Server=test;User ID=user;Pwd=old;Password=;Pwd=new;")]
    public void Review_MySqlPasswordAliasesFollowProviderSemantics(string connectionString)
    {
        MySqlConnectionStringBuilder provider = new(connectionString);
        DataSource source = new(DatabaseType.MySQL, connectionString);
        char expected = string.IsNullOrEmpty(provider.Password) ? 'M' : '0';

        Assert.AreEqual(expected, General(Config(source), source)[5]);
    }

    /// <summary>Ordinary application-name text must not be mistaken for an unresolved secret reference.</summary>
    [TestMethod]
    public void Review_LiteralReferenceTextDoesNotSuppressExplicitAuthentication()
    {
        DataSource source = new(DatabaseType.MSSQL, SQL_MI + "Application Name=tag@env(label);");

        Assert.AreEqual('1', General(Config(source), source)[5]);
    }

    /// <summary>Telemetry must not make an otherwise valid application name fail client validation.</summary>
    [DataTestMethod]
    [DataRow(DatabaseType.MSSQL, false, 64)]
    [DataRow(DatabaseType.MSSQL, false, 65)]
    [DataRow(DatabaseType.MSSQL, false, 66)]
    [DataRow(DatabaseType.MSSQL, false, 70)]
    [DataRow(DatabaseType.MSSQL, true, 67)]
    [DataRow(DatabaseType.DWSQL, false, 70)]
    [DataRow(DatabaseType.MSSQL, false, 128)]
    [DataRow(DatabaseType.MSSQL, false, 127)]
    public void Review_SqlApplicationNameFitsProviderLimit(DatabaseType type, bool hosted, int customLength)
    {
        Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, hosted ? "dab_hosted" : null);
        string customName = new('a', customLength);
        SqlConnectionStringBuilder original = new(SQL_PASSWORD) { ApplicationName = customName };
        using SqlConnection validOriginal = new(original.ConnectionString);
        DataSource source = new(type, original.ConnectionString);
        string updated = RuntimeConfigLoader.GetConnectionStringWithApplicationName(source.ConnectionString, Config(source), source);

        using SqlConnection actual = new(updated);
        SqlConnectionStringBuilder builder = new(actual.ConnectionString);
        Assert.IsTrue(builder.ApplicationName.Length <= 128);
        Assert.IsTrue(builder.ApplicationName.StartsWith(customName, StringComparison.Ordinal));
        Assert.AreEqual(updated, RuntimeConfigLoader.GetConnectionStringWithApplicationName(updated, Config(source), source));
    }

    /// <summary>The actual executor keeps metadata valid and per-user hashes complete after token growth.</summary>
    [TestMethod]
    public void Review_OboMetadataAndRequestNamesRespectClientLimit()
    {
        string customName = new('a', 70);
        DataSource source = new(DatabaseType.MSSQL, "Server=unit-test.invalid;Application Name=" + customName)
        {
            UserDelegatedAuth = new(Enabled: true),
        };
        RuntimeConfig config = Config(source);
        DataSource updated = source with
        {
            ConnectionString = RuntimeConfigLoader.GetConnectionStringWithApplicationName(source.ConnectionString, config, source),
        };
        config.UpdateDataSourceNameToDataSource(config.DefaultDataSourceName, updated);
        config = config with { DataSource = updated };
        using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
        using RuntimeConfigProvider provider = new(loader);
        HttpContextAccessor accessor = new();
        Mock<DbExceptionParser> parser = new(provider);
        MsSqlQueryExecutor executor = new(provider, parser.Object, NullLogger<IQueryExecutor>.Instance, accessor);

        using SqlConnection metadata = executor.CreateConnection(config.DefaultDataSourceName);
        Assert.IsTrue(new SqlConnectionStringBuilder(metadata.ConnectionString).ApplicationName.StartsWith(customName, StringComparison.Ordinal));
        Assert.AreEqual('M', GeneralFromConnectionString(metadata.ConnectionString)[5]);

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("iss", "https://issuer.invalid"),
                new Claim("oid", "test-user-one"),
            }, "test")),
        };
        using SqlConnection request = executor.CreateConnection(config.DefaultDataSourceName);
        string name = new SqlConnectionStringBuilder(request.ConnectionString).ApplicationName;
        Assert.IsTrue(name.Length <= 128);
        Assert.AreEqual(22, name.IndexOf('|'), "Per-user isolation hash must not be truncated.");
        Assert.IsTrue(name[23..].StartsWith(customName, StringComparison.Ordinal));
        Assert.IsTrue(ApplicationNameTelemetry.Decode(name).Any(line => line.Contains("managed-identity: M", StringComparison.Ordinal)));

        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("iss", "https://issuer.invalid"), new Claim("oid", "test-user-two"),
        }, "test"));
        using SqlConnection otherUser = executor.CreateConnection(config.DefaultDataSourceName);
        Assert.AreNotEqual(name[..22], new SqlConnectionStringBuilder(otherUser.ConnectionString).ApplicationName[..22]);
    }

    /// <summary>PostgreSQL's 63-byte truncation remains decodable, including hosted and UTF-8 prefixes.</summary>
    [DataTestMethod]
    [DataRow(false, "")]
    [DataRow(true, "")]
    [DataRow(true, "用户")]
    public void Review_PostgresByteTruncationKeepsGeneralPositions(bool hosted, string customName)
    {
        Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, hosted ? "dab_hosted" : null);
        DataSource source = new(DatabaseType.PostgreSQL, "Host=unit-test.invalid;Password=test-only;");
        string token = ApplicationNameTelemetry.EncodeTelemetryString(Config(source), source, _environment);
        string name = (customName.Length == 0 ? string.Empty : customName + ",") + token;
        StringBuilder serverName = new();
        int bytes = 0;
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 63)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            serverName.Append(rune.ToString());
        }

        IReadOnlyList<string> decoded = ApplicationNameTelemetry.Decode(serverName.ToString());
        Assert.AreEqual(6, decoded.Count(line => line.StartsWith("General >", StringComparison.Ordinal)));
        Assert.AreEqual(20, decoded.Count(line => line.StartsWith("Runtime >", StringComparison.Ordinal)));
        Assert.IsTrue(decoded.Any(line => line.Contains("managed-identity: 0", StringComparison.Ordinal)));
        Assert.IsTrue(Encoding.UTF8.GetByteCount(serverName.ToString()) <= 63);
    }

    /// <summary>The effective connection string, not a config placeholder, supplies per-pool auth.</summary>
    [DataTestMethod]
    [DataRow(DatabaseType.MSSQL, SQL_MI, '1')]
    [DataRow(DatabaseType.DWSQL, SQL_MI, '1')]
    [DataRow(DatabaseType.MSSQL, SQL_PASSWORD, '0')]
    [DataRow(DatabaseType.PostgreSQL, "Host=test;Password=test-only;", '0')]
    public void ConnectionStringOverride_ControlsManagedIdentity(DatabaseType type, string connectionString, char expected)
    {
        DataSource source = new(type, type == DatabaseType.PostgreSQL ? "Host=placeholder;" : "Server=placeholder;");
        RuntimeConfig config = Config(source);
        string updated = RuntimeConfigLoader.GetConnectionStringWithApplicationName(connectionString, config, source);

        Assert.AreEqual(expected, GeneralFromConnectionString(updated)[5]);
        Assert.AreEqual(source.ConnectionString, config.DataSource.ConnectionString, "Encoding must not mutate the input config.");
    }

    /// <summary>File-load overrides must reach the same authentication detection used for late config.</summary>
    [TestMethod]
    public void ParseConfig_ConnectionStringOverrideControlsManagedIdentity()
    {
        RuntimeConfig original = Config(new(DatabaseType.MSSQL, SQL_PASSWORD));
        bool parsed = RuntimeConfigLoader.TryParseConfig(original.ToJson(), out RuntimeConfig config, out _,
            new(doReplaceEnvVar: true), connectionString: SQL_MI);

        Assert.IsTrue(parsed);
        Assert.AreEqual('1', GeneralFromConnectionString(config.DataSource.ConnectionString)[5]);
    }

    /// <summary>The hosted path must describe the separately supplied connection string.</summary>
    [TestMethod]
    public async Task HostedConnectionStringOverride_ControlsManagedIdentity()
    {
        FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
        using RuntimeConfigProvider provider = new(loader);
        bool initialized = await provider.Initialize(Config(new(DatabaseType.MSSQL, SQL_PASSWORD)).ToJson(),
            graphQLSchema: null, connectionString: SQL_MI, accessToken: null,
            replacementSettings: new(doReplaceEnvVar: false));

        Assert.IsTrue(initialized);
        Assert.AreEqual('1', GeneralFromConnectionString(provider.GetConfig().DataSource.ConnectionString)[5]);
    }

    /// <summary>The runtime opt-out omits the new General section along with the rest of the payload.</summary>
    [TestMethod]
    public void BuildApplicationNameSegment_OptOutOmitsGeneral()
    {
        Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, "1");
        DataSource source = new(DatabaseType.MSSQL, SQL_MI);

        Assert.AreEqual(ProductInfo.DAB_USER_AGENT, ApplicationNameTelemetry.BuildApplicationNameSegment(Config(source), source));
    }

    /// <summary>Every defined General alphabet, including macOS versus missing, has a decoder legend.</summary>
    [DataTestMethod]
    [DataRow(0, 'W', "operating-system", "Windows")]
    [DataRow(0, 'L', "operating-system", "Linux")]
    [DataRow(0, 'M', "operating-system", "macOS")]
    [DataRow(0, 'O', "operating-system", "Other")]
    [DataRow(0, 'U', "operating-system", "Unknown")]
    [DataRow(0, 'Z', "operating-system", "unrecognized")]
    [DataRow(1, '0', "running-in-container", "disabled/no")]
    [DataRow(1, '1', "running-in-container", "enabled/yes")]
    [DataRow(1, 'M', "running-in-container", "missing")]
    [DataRow(2, 'L', "hosting-environment", "Local")]
    [DataRow(2, 'A', "hosting-environment", "Azure")]
    [DataRow(2, 'W', "hosting-environment", "AWS")]
    [DataRow(2, 'G', "hosting-environment", "GCP")]
    [DataRow(2, 'O', "hosting-environment", "Other")]
    [DataRow(2, 'M', "hosting-environment", "missing")]
    [DataRow(2, 'Z', "hosting-environment", "unrecognized")]
    [DataRow(3, 'C', "azure-hosting-service", "Container Apps")]
    [DataRow(3, 'K', "azure-hosting-service", "AKS")]
    [DataRow(3, 'S', "azure-hosting-service", "App Service")]
    [DataRow(3, 'I', "azure-hosting-service", "ACI")]
    [DataRow(3, 'O', "azure-hosting-service", "Other")]
    [DataRow(3, 'N', "azure-hosting-service", "Not Azure")]
    [DataRow(3, 'M', "azure-hosting-service", "missing")]
    [DataRow(3, 'Z', "azure-hosting-service", "unrecognized")]
    [DataRow(4, '0', "multiple-data-sources", "disabled/no")]
    [DataRow(4, '1', "multiple-data-sources", "enabled/yes")]
    [DataRow(4, 'M', "multiple-data-sources", "missing")]
    [DataRow(5, '0', "managed-identity", "disabled/no")]
    [DataRow(5, '1', "managed-identity", "enabled/yes")]
    [DataRow(5, 'M', "managed-identity", "missing")]
    public void DecodeGeneral_DescribesAlphabet(int position, char value, string name, string description)
    {
        char[] general = "W0LN00".ToCharArray();
        general[position] = value;
        IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode($"dab_oss_1.2.3+XXSX|{new string(general)}|M|M+");

        CollectionAssert.Contains(lines.ToArray(), $"General > {name}: {value} ({description})");
    }

    /// <summary>Old tokens with an empty General section retain the same runtime/entity positions.</summary>
    [TestMethod]
    public void DecodeGeneral_EmptyLegacySectionIsCompatible()
    {
        IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode("dab_oss_1.2.3+XXSX||1|0+");

        Assert.IsFalse(lines.Any(line => line.StartsWith("General >", StringComparison.Ordinal)));
        CollectionAssert.Contains(lines.ToArray(), "Runtime > runtime.rest.enabled: 1 (enabled/yes)");
        CollectionAssert.Contains(lines.ToArray(), "Entity > entities.any.table: 0 (disabled/no)");
    }

    /// <summary>All truncation points within General decode only the surviving positions.</summary>
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public void DecodeGeneral_TruncatedSectionIsTolerated(int survivingFlags)
    {
        IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode("dab_hosted_1.2.3+XXSX|" + "L1AC01"[..survivingFlags]);

        Assert.AreEqual(survivingFlags, lines.Count(line => line.StartsWith("General >", StringComparison.Ordinal)));
        Assert.IsFalse(lines.Any(line => line.StartsWith("Runtime >", StringComparison.Ordinal)));
        Assert.IsFalse(lines.Any(line => line.StartsWith("Entity >", StringComparison.Ordinal)));
    }

    /// <summary>Neither host values nor connection-string values appear in the token or decoded output.</summary>
    [TestMethod]
    public void EncodeGeneral_DoesNotExposeEnvironmentOrCredentials()
    {
        const string sensitiveValue = "private-value-never-emit";
        ApplicationNameTelemetryEnvironment environment = Capture(new()
        {
            ["WEBSITE_SITE_NAME"] = sensitiveValue,
            [ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR] = sensitiveValue,
        });
        DataSource source = new(DatabaseType.MSSQL, $"Server={sensitiveValue};User ID={sensitiveValue};Password={sensitiveValue};");
        string token = ApplicationNameTelemetry.EncodeTelemetryString(Config(source), source, environment);

        Assert.IsFalse(token.Contains(sensitiveValue, StringComparison.Ordinal));
        Assert.IsFalse(string.Join('\n', ApplicationNameTelemetry.Decode(token)).Contains(sensitiveValue, StringComparison.Ordinal));
    }

    private static ApplicationNameTelemetryEnvironment Capture(Dictionary<string, string> values) =>
        ApplicationNameTelemetryEnvironment.Capture(name => values.TryGetValue(name, out string value) ? value : null);

    private static RuntimeConfig Config(DataSource source) =>
        new(Schema: "test", DataSource: source, Entities: new(new Dictionary<string, Entity>()));

    private static string General(RuntimeConfig config, DataSource liveDataSource = null) =>
        Sections(ApplicationNameTelemetry.EncodeTelemetryString(config, liveDataSource, _environment))[1];

    private static string[] Sections(string token)
    {
        string[] sections = token[(token.IndexOf('+') + 1)..^1].Split('|');
        Assert.AreEqual(4, sections.Length);
        Assert.AreEqual(6, sections[1].Length);
        return sections;
    }

    private static string GeneralFromConnectionString(string connectionString)
    {
        DbConnectionStringBuilder builder = new() { ConnectionString = connectionString };
        return Sections((string)builder["Application Name"])[1];
    }
}