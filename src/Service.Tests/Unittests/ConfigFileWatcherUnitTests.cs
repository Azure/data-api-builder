// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests;

[TestClass]
public class ConfigFileWatcherUnitTests
{

    private static string GenerateRuntimeSectionStringFromParams(
        string restPath,
        string gqlPath,
        bool restEnabled,
        bool gqlEnabled,
        bool gqlIntrospection,
        HostMode mode
        )
    {
        string runtimeString = @"
{
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""Server=test;Database=test;User ID=test;"",
    ""options"": {
      ""set-session-context"": true
    }
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": " + restEnabled.ToString().ToLower() + @",
      ""path"": """ + restPath + @"""
    },
    ""graphql"": {
      ""enabled"": " + gqlEnabled.ToString().ToLower() + @",
      ""path"": """ + gqlPath + @""",
      ""allow-introspection"": " + gqlIntrospection.ToString().ToLower() + @"
    },
    ""host"": {
      ""cors"": {
        ""origins"": [
          ""http://localhost:5000""
        ],
        ""allow-credentials"": false
      },
      ""authentication"": {
        ""provider"": ""StaticWebApps""
      },
      ""mode"": """ + mode + @"""
    }
  },
  ""entities"": {}
}";
        return runtimeString;
    }

    /// <summary>
    /// Use the file system (not mocked) to create a hot reload
    /// scenario of the REST runtime options and verify that we
    /// correctly hot reload those options.
    /// NOTE: This test is ignored until we have the possibility of turning
    /// on hot reload.
    /// </summary>
    [TestMethod]
    public void HotReloadConfigRestRuntimeOptions()
    {
        // Arrange
        // 1. Setup the strings that are turned into our initital and runtime config to reload.
        // 2. Generate initial runtimeconfig, start file watching, and assert we have valid initial values.
        string initialRestPath = "/api";
        string updatedRestPath = "/rest";
        string initialGQLPath = "/api";
        string updatedGQLPath = "/gql";

        bool initialRestEnabled = true;
        bool updatedRestEnabled = false;
        bool initialGQLEnabled = true;
        bool updatedGQLEnabled = false;

        bool initialGQLIntrospection = true;
        bool updatedGQLIntrospection = false;

        HostMode initialMode = HostMode.Development;
        HostMode updatedMode = HostMode.Production;

        string initialConfig = GenerateRuntimeSectionStringFromParams(
            initialRestPath,
            initialGQLPath,
            initialRestEnabled,
            initialGQLEnabled,
            initialGQLIntrospection,
            initialMode);

        string updatedConfig = GenerateRuntimeSectionStringFromParams(
            updatedRestPath,
            updatedGQLPath,
            updatedRestEnabled,
            updatedGQLEnabled,
            updatedGQLIntrospection,
            updatedMode);

        string configName = "hotreload-config.json";
        if (File.Exists(configName))
        {
            File.Delete(configName);
        }

        // Not using mocked filesystem so we pick up real file changes for hot reload
        FileSystem fileSystem = new();
        fileSystem.File.WriteAllText(configName, initialConfig);
        FileSystemRuntimeConfigLoader configLoader = new(fileSystem, handler: null, configName, string.Empty);
        RuntimeConfigProvider configProvider = new(configLoader);

        // Must GetConfig() to start file watching
        RuntimeConfig runtimeConfig = configProvider.GetConfig();

        // assert we have a valid config
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(initialRestEnabled, runtimeConfig.Runtime.Rest.Enabled);
        Assert.AreEqual(initialRestPath, runtimeConfig.Runtime.Rest.Path);
        Assert.AreEqual(initialGQLEnabled, runtimeConfig.Runtime.GraphQL.Enabled);
        Assert.AreEqual(initialGQLPath, runtimeConfig.Runtime.GraphQL.Path);
        Assert.AreEqual(initialGQLIntrospection, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
        Assert.AreEqual(initialMode, runtimeConfig.Runtime.Host.Mode);

        // Simulate change to the config file
        fileSystem.File.WriteAllText(configName, updatedConfig);

        // Give ConfigFileWatcher enough time to hot reload the change
        System.Threading.Thread.Sleep(1000);

        // Act
        // 1. Hot reload the runtime config
        runtimeConfig = configProvider.GetConfig();

        // Assert
        // 1. Assert we have the correct values after a hot reload.
        Assert.AreEqual(updatedRestEnabled, runtimeConfig.Runtime.Rest.Enabled);
        Assert.AreEqual(updatedRestPath, runtimeConfig.Runtime.Rest.Path);
        Assert.AreEqual(updatedGQLEnabled, runtimeConfig.Runtime.GraphQL.Enabled);
        Assert.AreEqual(updatedGQLPath, runtimeConfig.Runtime.GraphQL.Path);
        Assert.AreEqual(updatedGQLIntrospection, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
        // Mode can not be hot-reloaded.
        Assert.AreNotEqual(updatedMode, runtimeConfig.Runtime.Host.Mode);

        if (File.Exists(configName))
        {
            File.Delete(configName);
        }
    }

    #region ConfigFileWatcher NewFileContentsDetected event invocation tests

    private const string UNEXPECTED_INVOCATION_COUNT_ERR = "Unexpected number of invocations of the NewFileContentsDetected event.";
    private const string FILECHANGE_EVENT_NOT_RAISED_ERR = "The file system's file-changed event was not raised.";

    /// <summary>
    /// This test validates that overwriting a file with different content
    /// results in ConfigFileWatcher's NewFileContentsDetected event being raised.
    /// Internally, ConfigFileWatcher.NewFileContentsDetected is raised when the hash
    /// of the file's contents differ from what was previously detected.
    /// - Original File Content: FirstValue
    /// - New File Content: SecondValue
    /// The config file name is specific to this test in order to avoid concurrently
    /// running tests from stepping over each other due to acquiring a handle(s) to the file.
    /// </summary>
    [TestMethod]
    public void ConfigFileWatcher_NotifiedOfOneNetNewChanges()
    {
        // Arrange
        int fileChangeNotificationsReceived = 0;
        string configName = "ConfigFileWatcher_NotifiedOfOneNetNewChanges.json";

        // Mock filesystem calls for when FileUtilities.ComputeHash() utilizes the filesystem.
        // For this test, we assume the file exists because we are validating the behavior
        // of differing config file contents triggering a NewFileContentsDetected event.
        IFileSystem fileSystem = Mock.Of<IFileSystem>();
        Mock.Get(fileSystem).Setup(fs => fs.Directory.GetCurrentDirectory()).Returns(Directory.GetCurrentDirectory());
        Mock.Get(fileSystem).Setup(fs => fs.File.Exists(It.IsAny<string>())).Returns(true);

        // Mock file system to return different byte arrays for each read which occurs in
        // FileUtilities.ComputeHash() called by ConfigFileWatcher.
        // The first read occurs during ConfigFileWatcher's initialization.
        // The subsequent reads trigger the behavior this test validates.
        // Because a real file system returns >1 file changed event, there will be >1
        // invocation of fs.File.ReadAllBytes.
        // The first return of "SecondValue" will trigger a NewFileContentsDetected event.
        // The second return of "SecondValue" will NOT trigger a NewFileContentsDetected event
        // because that event has already been raised for "SecondValue".
        Mock.Get(fileSystem).SetupSequence(fs => fs.File.ReadAllBytes(It.IsAny<string>()))
            .Returns(Encoding.UTF8.GetBytes("FirstValue"))
            .Returns(Encoding.UTF8.GetBytes("SecondValue"))
            .Returns(Encoding.UTF8.GetBytes("SecondValue"));

        Mock<IFileSystemWatcher> fileSystemWatcherMock = new();
        fileSystemWatcherMock.Setup(mock => mock.FileSystem).Returns(fileSystem);

        ConfigFileWatcher fileWatcher = new(fileSystemWatcherMock.Object, directoryName: Directory.GetCurrentDirectory(), configFileName: configName);
        fileWatcher.NewFileContentsDetected += (sender, e) =>
        {
            // For testing, modification of fileChangeNotificationsRecieved
            // should be atomic. 
            Interlocked.Increment(ref fileChangeNotificationsReceived);
        };

        // Track the number of invocations so far so we can prove that
        // fileSystemWatcherMock.Raise() actually triggers a "file changed" event.
        int numberFileSystemWatcherMockInvocations = fileSystemWatcherMock.Invocations.Count;

        // Act
        // Manually induce two "file changed" events to simulate a file change
        // which is detected by ConfigFileWatcher's OnConfigFileChange() method.
        // The two events result in two filesystem reads returning "SecondValue".
        // This test ensures the second instance of "SecondValue" results in a no-op
        // as no file changes occurred and NewFileContentsDetected was already raised.
        // Upon detecting a net-new config (due to differing hash),
        // ConfigFileWatcher triggers its NewFileContentsDetected event.
        int expectedInvocationCount = 2;
        for (int invocations = 1; invocations <= expectedInvocationCount; invocations++)
        {
            fileSystemWatcherMock.Raise(mock => mock.Changed += null, this, new FileSystemEventArgs(
                changeType: WatcherChangeTypes.Changed,
                directory: fileSystem.Directory.GetCurrentDirectory(),
                name: configName));
        }

        // Assert
        // Even though the ConfigFileWatcher's NewFileContentsDetected is raised,
        // we still expect the underlying FileSystemWatcher's file changed event to
        // have been raised more than once.
        // The ConfigFileWatcher's job is to ensure it only raises its NewFileContentsDetected
        // event when the new file's hash differs from the currently tracked file hash.
        Assert.AreEqual(
            expected: expectedInvocationCount,
            actual: fileSystemWatcherMock.Invocations.Count - numberFileSystemWatcherMockInvocations,
            message: FILECHANGE_EVENT_NOT_RAISED_ERR);
        Assert.AreEqual(
            expected: 1,
            actual: fileChangeNotificationsReceived,
            message: UNEXPECTED_INVOCATION_COUNT_ERR);
    }

    /// <summary>
    /// This test validates that overwriting a file with the same contents does not
    /// result in ConfigFileWatcher's NewFileContentsDetected event being raised.
    /// Internally, ConfigFileWatcher.NewFileContentsDetected is raised when the hash
    /// of the file contents differ from what was previously detected.
    /// In this test, the file contents are the same: MyValue -> MyValue.
    /// The config file name is specific to this test in order to avoid concurrently
    /// running tests from stepping over each other due to acquiring a handle(s) to the file.
    /// </summary>
    [TestMethod]
    public void ConfigFileWatcher_NotifiedOfZeroNetNewChange()
    {
        // Arrange
        int fileChangeNotificationsReceived = 0;
        string configName = "ConfigFileWatcher_NotifiedOfZeroNetNewChange.json";

        // Mock filesystem calls for when FileUtilities.ComputeHash() utilizes the filesystem.
        // For this test, we assume the file exists because we are validating the behavior
        // of differing config file contents triggering a NewFileContentsDetected event.
        IFileSystem fileSystem = Mock.Of<IFileSystem>();
        Mock.Get(fileSystem).Setup(fs => fs.Directory.GetCurrentDirectory()).Returns(Directory.GetCurrentDirectory());
        Mock.Get(fileSystem).Setup(fs => fs.File.Exists(It.IsAny<string>())).Returns(true);

        // Mock file system to return different byte arrays for each read which occurs in
        // FileUtilities.ComputeHash() called by ConfigFileWatcher.
        // The first read occurs during ConfigFileWatcher's initialization
        // The second read occurs during the manual invocation of a "file changed" event.
        Mock.Get(fileSystem).SetupSequence(fs => fs.File.ReadAllBytes(It.IsAny<string>()))
            .Returns(Encoding.UTF8.GetBytes("FirstValue"))
            .Returns(Encoding.UTF8.GetBytes("FirstValue"));

        Mock<IFileSystemWatcher> fileSystemWatcherMock = new();
        fileSystemWatcherMock.Setup(mock => mock.FileSystem).Returns(fileSystem);

        ConfigFileWatcher fileWatcher = new(fileSystemWatcherMock.Object, directoryName: Directory.GetCurrentDirectory(), configFileName: configName);
        fileWatcher.NewFileContentsDetected += (sender, e) =>
        {
            // For testing, modification of fileChangeNotificationsRecieved
            // should be atomic. 
            Interlocked.Increment(ref fileChangeNotificationsReceived);
        };

        // Track the number of invocations so far so we can prove that
        // fileSystemWatcherMock.Raise() actually triggers a "file changed" event.
        int numberFileSystemWatcherMockInvocations = fileSystemWatcherMock.Invocations.Count;

        // Act
        // Manually induce a "file changed" event to simulate a file change
        // which is detected by ConfigFileWatcher's OnConfigFileChange() method.
        // Upon detecting the same config (due to same hash), ConfigFileWatcher
        // skips triggering its NewFileContentsDetected event.
        fileSystemWatcherMock.Raise(mock => mock.Changed += null, this, new FileSystemEventArgs(
            changeType: WatcherChangeTypes.Changed,
            directory: fileSystem.Directory.GetCurrentDirectory(),
            name: configName));

        // Assert
        // Even though the ConfigFileWatcher's NewFileContentsDetected is not raised,
        // we still expected the underlying FileSystemWatcher's file changed event to be raised.
        // The ConfigFileWatcher's job is to ensure it only raises its NewFileContentsDetected
        // event when the new file's hash differs from the currently tracked file hash.
        Assert.AreEqual(
            expected: 1,
            actual: fileSystemWatcherMock.Invocations.Count - numberFileSystemWatcherMockInvocations,
            message: FILECHANGE_EVENT_NOT_RAISED_ERR);
        Assert.AreEqual(
            expected: 0,
            actual: fileChangeNotificationsReceived,
            message: UNEXPECTED_INVOCATION_COUNT_ERR);
    }
    #endregion
}
