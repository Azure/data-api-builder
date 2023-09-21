// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class to represent boolean values in CLI. This is required over primitive boolean types because of the limitation of the CommandLineParser library
/// where if a boolean option is included in the CLI command, it is set as true.
/// Doesn't matter what value we specify for the option as that value is ignored.
/// </summary>
public enum CliBoolean
{
    // The enum value None is required to determine whether a value was provided for a CLI option. In case the option is not included in the init command,
    // the enum gets assigned a value of an uninitialized enum i.e. 0 (here 'None').
    None,
    True,
    False
}
