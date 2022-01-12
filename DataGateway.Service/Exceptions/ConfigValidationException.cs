using System;
using System.Collections.Generic;

namespace Azure.DataGateway.Service.Exceptions
{

    /// <summary>
    /// Used to avoid throwing generic Exception for configuration exceptions
    /// </summary>
#pragma warning disable CA1032 // Supressing since we only use the custom constructor
    public class ConfigValidationException : Exception
    {
        /// <summary>
        /// Gets thrown with a message and a validation stack which informs what
        /// section of the config was being validated when the exception was thrown
        /// </summary>
        /// <param name="message"></param>
        /// <param name="validationStack">
        /// Upper most element is the smallest context
        /// e.g. For Largest Ctx > Smaller Ctx > Smallest Ctx the stack is
        /// TOP: Smallest Ctx | Smaller Ctx | Largest Ctx
        /// </param>
        public ConfigValidationException(string message, Stack<string> validationStack) : base($"{PrettyPrintValidationStack(validationStack)} {message}") { }

        /// <summary>
        /// Print the reversed validation stack since the validation stack
        /// contains the smallest context at the top and the largest at the bottom
        /// </summary>
        private static string PrettyPrintValidationStack(Stack<string> validationStack)
        {
            string[] stackArray = validationStack.ToArray();
            Array.Reverse(stackArray);
            return $"In {string.Join(" > ", stackArray)}: ";
        }
    }
}
