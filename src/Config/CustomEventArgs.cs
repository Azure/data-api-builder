// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    public class CustomEventArgs : EventArgs
    {
        public string Message { get; set; }

        public CustomEventArgs(string message)
        {
            Message = message;
        }
    }
}
