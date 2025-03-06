// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public enum EasyAuthType
{
    StaticWebApps,
    AppService,

    /// <summary>
    /// A synonym for <see cref="StaticWebApps"/>
    /// </summary>
    EasyAuth,

    /// <summary>
    /// Another synonym for <see cref="StaticWebApps"/>, like <see cref="EasyAuth"/>
    /// </summary>
    None
}
