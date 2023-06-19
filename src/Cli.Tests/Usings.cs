// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

global using System.Diagnostics;
global using System.Text.Json;
global using Azure.DataApiBuilder.Config;
global using Azure.DataApiBuilder.Service.Exceptions;
global using Microsoft.Extensions.Logging;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Moq;
global using Newtonsoft.Json.Linq;
global using static Azure.DataApiBuilder.Config.RuntimeConfigPath;
global using static Cli.ConfigGenerator;
global using static Cli.Tests.TestHelper;
global using static Cli.Utils;
