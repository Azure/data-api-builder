// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

global using System.Diagnostics;
global using System.IO.Abstractions;
global using System.IO.Abstractions.TestingHelpers;
global using System.Text.Json;
global using Azure.DataApiBuilder.Config;
global using Azure.DataApiBuilder.Config.ObjectModel;
global using Azure.DataApiBuilder.Service.Exceptions;
global using Cli.Commands;
global using Microsoft.Extensions.Logging;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Moq;
global using Newtonsoft.Json.Linq;
global using static Azure.DataApiBuilder.Config.FileSystemRuntimeConfigLoader;
global using static Cli.ConfigGenerator;
global using static Cli.Tests.TestHelper;
global using static Cli.Utils;
