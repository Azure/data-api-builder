// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Cli.Tests
{
    [TestClass]
    public class StaleReferenceConfigFileTest : VerifyBase
    {
        private IFileSystem? _fileSystem;
        private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;
        private ILogger<Program>? _cliLogger;

        //reference config file
        private const string MSSQL_REFERENCE_CONFIG_JSON = "mssql_referenceConfig.json";
        private const string MYSQL_REFERENCE_CONFIG_JSON = "mysql_referenceConfig.json";
        private const string POSTGRESQL_REFERENCE_CONFIG_JSON = "postgresql_referenceConfig.json";
        private const string COSMOSDB_NOSQL_REFERENCE_CONFIG_JSON = "cosmosdb_nosql_referenceConfig.json";
        private const string DWSQL_REFERENCE_CONFIG_JSON = "dwsql_referenceConfig.json";

        //CLI command files
        private const string MSSQL_COMMANDS_TXT = "mssql-commands.txt";

        [TestInitialize]
        public void TestInitialize()
        {
            string mssqlReferenceConfig = File.ReadAllText("dab-config.MsSql.json");
            string mssqlCommands = File.ReadAllText(MSSQL_COMMANDS_TXT);
            /*            string replacedCommands = Regex.Replace(mssqlCommands, @"", """);*/
            mssqlCommands = mssqlCommands.Replace("\\\"", "");

            string serialziedString = JsonSerializer.Serialize(mssqlReferenceConfig);

            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            fileSystem.AddFile(
            "mssql-commands.txt",
                new MockFileData(mssqlCommands));

            fileSystem.AddFile(
                MSSQL_REFERENCE_CONFIG_JSON,
                new MockFileData(serialziedString));

            _fileSystem = fileSystem;

            _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem);

            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();
            _cliLogger = loggerFactory.CreateLogger<Program>();
            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _fileSystem = null;
            _runtimeConfigLoader = null;
            _cliLogger = null;
        }

        ///<summary>
        ///
        /// </summary>
        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, DisplayName = "mssql")]
        public void ValidateIfReferenceConfigFileIsUptoDate(DatabaseType dbtype)
        {
            string cliCommandsFileName = $"{dbtype.ToString().ToLower()}-commands.txt";
            //string referenceConfigFileName = $"dab-reference-config.{dbtype.ToString().ToLower()}.json";

            string[] cliCommands = _fileSystem!.File.ReadAllLines(cliCommandsFileName, Encoding.Default);

            /*foreach(string cliCommand in cliCommands)
            {*/
            string cliCommand = cliCommands[0]; 
                List<string> commandArgs = new ();
                int n = cliCommand.Length;
                int i = 0;
                while(i < n)
                {
                    int j;
                    int sizeOfSubString;
                    if (cliCommand[i] == '"')
                    {
                        j = cliCommand.IndexOf('"',i+1);
                        sizeOfSubString = j - i + 1;
                    }
                    else
                    {
                        j = cliCommand.IndexOf(' ', i + 1);
                        if(j == -1)
                        {
                            j = n;
                        }

                        sizeOfSubString = j - i;
                    }

                    string commandArg = cliCommand.Substring(i, sizeOfSubString);
                    Console.WriteLine(commandArg);

                    commandArgs.Add(@""+ commandArg +"");
                    i = (cliCommand[i] == '"') ? j+2 : j + 1;
                }

                Program.Execute(commandArgs.ToArray(), _cliLogger!, _fileSystem!, _runtimeConfigLoader!);
            //}

/*            string referenceConfigFile = _fileSystem.File.ReadAllText(MSSQL_REFERENCE_CONFIG_JSON);
            string cliGeneratedConfigFile = _fileSystem.File.ReadAllText($"dab-config.MsSql.json");*/

        }
    }
}
