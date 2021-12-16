using System;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Exceptions
{
    [System.Serializable]
    public class UpdateMutationHasNoUpdatesException : GraphQLUserLevelException
    {
        public static readonly string MESSAGE = "Update mutation does not contain new values to update";
        public UpdateMutationHasNoUpdatesException() : base(MESSAGE) { }
        public UpdateMutationHasNoUpdatesException(string message) : base(MESSAGE) { }
        public UpdateMutationHasNoUpdatesException(string message, Exception inner) : base(MESSAGE, inner) { }
    }
}
