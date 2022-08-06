using System.Collections;
using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Parsers
{
    /// <summary>
    /// This class is used to handle the parsing of the
    /// Source that is associated with a given Entity. The
    /// Source contains the schema name and table name in string
    /// format with the separation between the two names handled by
    /// a ".", this parser, when its parsing function is invoked,
    /// will return these values as individual strings, if they exist,
    /// as a tuple (string, string)
    /// </summary>
    public class EntitySourceNamesParser
    {
        /// <summary>
        /// Function to get the schema and table from the parameter input string.
        /// </summary>
        /// <param name="input">Path parameter in the format of "table", "schema.table",
        /// "[sch.ema].table, "schema.[ta]]ble]", "[sche]]ma].[ta.ble]".
        /// Surround with [] when using special characters "[" or "]" or "." in the schema/table name
        /// The user also needs to escape a "]" with another "]"</param>
        /// <returns>
        /// Returns a tuple of the schema and table. 
        /// </returns>
        /// <exception cref="DataApiBuilderException">If the table or parsed schema is null it will throw this exception.
        /// </exception>
        public static (string?, string) ParseSchemaAndTable(string input)
        {
            (string?, string?) schemaTable = ParseSchemaAndTableHelper(input);
            if (string.IsNullOrEmpty(schemaTable.Item2))
            {
                throw new DataApiBuilderException(message: $"Table is empty for input={input}",
                                               statusCode: HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaTable.Item1, schemaTable.Item2);
        }

        /// <summary>
        /// Helper function to get the schema and table from the parameter input string.
        /// </summary>
        /// <param name="input">Path parameter in the format of "table", "schema.table", 
        /// "[sch.ema].table, "schema.[ta]]ble]", "[sche]]ma].[ta.ble]". 
        /// Surround with [] when using special characters "[" or "]" or "." in the schema/table name
        /// The user also needs to escape a "]" with another "]"</param>
        /// <returns>
        /// Returns a tuple of the schema and table. 
        /// </returns>
        /// <exception cref="DataApiBuilderException">If the table or parsed schema is null it will throw this exception.
        /// </exception>
        private static (string?, string?) ParseSchemaAndTableHelper(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new DataApiBuilderException(message: "Input is null or empty string",
                                               statusCode: HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            if (input.EndsWith('.'))
            {
                throw new DataApiBuilderException(message: "Input cannot end with '.'",
                                               statusCode: HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            ArrayList tokens = new();
            string? nextToken = input;
            while (nextToken != null)
            {
                (string?, string?) tokenAndNextToken = GetTokenAndNextToken(nextToken);
                tokens.Add(tokenAndNextToken.Item1);
                nextToken = tokenAndNextToken.Item2;

                //If there is more than two tokens, it means their input was not valid.
                if (tokens.Count > 2)
                {
                    throw new DataApiBuilderException(message: $"Invalid number of tokens for={input}. Number of tokens is=${tokens.Count}",
                                                   statusCode: HttpStatusCode.ServiceUnavailable,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }

            // If there is one token only that means we only parsed the table name. We will use the default schema.
            if (tokens.Count == 1)
            {
                return (string.Empty, (string?)tokens[0]);
            }

            // If we parsed two tokens that means we parsed the schema and table name.
            return ((string?)tokens[0], (string?)tokens[1]);
        }

        /// <summary>
        /// Function that extracts the token and returns the string starting from the next token.
        /// </summary>
        /// <param name="input">Path parameter in the format of "table", "schema.table", 
        /// "[sch.ema].table, "schema.[ta]]ble]", "[sche]]ma].[ta.ble]". 
        /// Surround with [] when using special characters "[" or "]" or "." in the schema/table name
        /// The user also needs to escape a "]" with another "]"</param>
        ///  <param name="startIndex">Optional parameter of the index to start parsing the token from.</param>
        /// <returns>
        /// Returns a tuple of the token and the string starting from the next token.
        /// </returns>
        /// <exception cref="DataApiBuilderException">If the token is unable to be parsed it will throw this exception.
        /// </exception>
        private static (string?, string?) GetTokenAndNextToken(string input, int startIndex = 0)
        {
            bool startsWithBracket = input[startIndex] == '[';
            int i = startIndex;
            while (i < input.Length)
            {
                if (!startsWithBracket)
                {
                    if (input[i] == '.')
                    {
                        // ^ is startIndex and i is current index
                        // Ex: abc.xyz
                        //     ^  i
                        // Return "abc" as the token and the index of x as the next starting index
                        //
                        return (input[startIndex..i], input.Substring(i + 1));
                    }

                    if (input[i] == ']' || input[i] == '[')
                    {
                        // ^ is startIndex and i is current index
                        // Ex: abc]xyz
                        //     ^  i
                        // Return exception because we encountered a ']' or '[',  and the token was not surrounded by "[" and "]".
                        //
                        throw new DataApiBuilderException(message: "Token is not surrounded by '[' and ']'.",
                                                       statusCode: HttpStatusCode.ServiceUnavailable,
                                                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                } // close of if of !startsWithBracket
                else
                {
                    if (input[i] == ']' && i + 1 < input.Length && input[i + 1] == '.')
                    {
                        // ^ is startIndex and i is current index
                        // Ex: [ab.c].xyz
                        //     ^    i
                        // Return "ab.c" as the token and the index of x as the next starting index
                        //
                        return (input[(startIndex + 1)..i], input.Substring(i + 2));
                    }
                    else if (input[i] == ']' && i + 1 < input.Length && input[i + 1] == ']')
                    {
                        if (i + 2 >= input.Length)
                        {
                            // ^ is startIndex and i is current index
                            // Ex: [abc]]
                            //     ^   i
                            // Return exception because we encountered ]] at the end of the string, and there is no closing "]"
                            throw new DataApiBuilderException(message: "Token does not have closing ']'.",
                                                           statusCode: HttpStatusCode.ServiceUnavailable,
                                                           subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                        }

                        // We increase by 2 so that we skip over the escaped "]"
                        i += 2;

                        continue;
                    }
                    else if (input[i] == ']' && i + 1 < input.Length && input[i + 1] != ']')
                    {
                        // ^ is startIndex and i is current index
                        // Ex: [abc]xyz
                        //     ^   i
                        // Return exception because we encountered "]" and the character after "]" is not a "."
                        throw new DataApiBuilderException(message: "Token has invalid character next to ']'. Allowed characters are '.' and ']'.",
                                                       statusCode: HttpStatusCode.ServiceUnavailable,
                                                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                }  // close of else of !startsWithBracket

                i += 1;
            }  // close of while loop

            // Special cases for parsing tokens at the end of the string.
            if (startsWithBracket)
            {
                if (!input.EndsWith(']'))
                {
                    // ^ is startIndex and i is current index
                    // Ex: [abcdef
                    //     ^   i
                    // Return exception because there is no corresponding closing "]".
                    //
                    throw new DataApiBuilderException(message: "Token does not have corresponding closing ']'.",
                                                   statusCode: HttpStatusCode.ServiceUnavailable,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
                // ^ is startIndex and i is current index
                // Ex: xyz.[abc.def]
                //         ^        i
                // Return "abc.def" as the token and the input length as the index of the next token (it wont look for more tokens after this)
                //
                return (input[(startIndex + 1)..(input.Length - 1)], null);
            }
            else
            {
                // ^ is startIndex and i is current index
                // Ex: abc.def
                //         ^  i
                // Return "def" as the token and the input length as the index of the next token (it wont look for more tokens after this)
                //
                return (input[startIndex..(input.Length)], null);
            }
        }
    }
}
