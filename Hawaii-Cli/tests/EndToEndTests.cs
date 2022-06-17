namespace Hawaii.Cli;

[TestClass]
public class EndToEndTests
{
    [TestMethod]
    public void TestInitForCosmosDB()
    {
        string[] args = { "init", "-n", "hawaii-config-test", "--database-type", "cosmos", "--connection-string", "localhost:5000", "--cosmos-database", "graphqldb", "--cosmos-container", "planet", "--graphql-schema", "schema.gql" };
        Program.Main(args);
        string jsonString = File.ReadAllText("hawaii-config-test.json");

        RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, RuntimeConfig.GetDeserializationOptions());

        if (runtimeConfig is null)
        {
            Assert.Fail("Config was not genertaed correctly.");
        }

        Assert.AreEqual(DatabaseType.cosmos, runtimeConfig.DatabaseType);
        Assert.IsNotNull(runtimeConfig.CosmosDb);
        Assert.AreEqual("graphqldb", runtimeConfig.CosmosDb.Database);
        Assert.AreEqual("planet", runtimeConfig.CosmosDb.Container);
        Assert.AreEqual("schema.gql", runtimeConfig.CosmosDb.GraphQLSchemaPath);
        JsonElement jsonRestSettings = (JsonElement)runtimeConfig.RuntimeSettings[GlobalSettingsType.Rest];

        RestGlobalSettings? restGlobalSettings = JsonSerializer.Deserialize<RestGlobalSettings>(jsonRestSettings, RuntimeConfig.GetDeserializationOptions());
        Assert.IsNotNull(restGlobalSettings);
        Assert.IsFalse(restGlobalSettings.Enabled);
        Assert.IsNotNull(runtimeConfig.HostGlobalSettings);
        Assert.AreEqual(HostModeType.Production, runtimeConfig.HostGlobalSettings.Mode);

    }
}
