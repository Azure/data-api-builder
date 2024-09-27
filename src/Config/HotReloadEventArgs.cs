// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

    public class HotReloadEventArgs : EventArgs
    {
        public string Message { get; set; }

        public HotReloadEventArgs(string message)
        {
            Message = message;
        }
    }
