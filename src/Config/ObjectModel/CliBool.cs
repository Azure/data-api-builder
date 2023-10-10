// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public enum CliBool
{
    // The enum value None is required to determine whether a value was provided for a CLI option. In case the option is not included in the init command,
    // the enum gets assigned a value of an uninitialized enum i.e. 0 (here 'None').
    None,
    True,
    False
}
