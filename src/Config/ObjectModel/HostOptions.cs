// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HostOptions(CorsOptions? Cors, AuthenticationOptions? Authentication, HostMode Mode = HostMode.Production);
