using System;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Exceptions
{
    /// <summary>
    /// Thrown when an update mutation in GraphQL has none of the optional parameters
    /// used to determine the values to update
    /// </summary>
    [System.Serializable]
    public class UpdateMutationHasNoUpdatesException : GraphQLUserLevelException
    {
        public static readonly string MESSAGE = "Update mutation does not contain new values to update";
        public UpdateMutationHasNoUpdatesException() : base(MESSAGE) { }
        public UpdateMutationHasNoUpdatesException(string message) : base(MESSAGE) { }
        public UpdateMutationHasNoUpdatesException(string message, Exception inner) : base(MESSAGE, inner) { }
    }
}
