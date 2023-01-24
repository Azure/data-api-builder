using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Auth
{
    /// <summary>
    /// Interface for authorization decision-making. Each method performs lookups within a
    /// structure representing permissions defined in the runtime config.
    /// </summary>
    public interface IAuthorizationResolver
    {
        /// <summary>
        /// Representation of authorization permissions for each entity in the runtime config.
        /// </summary>
        public Dictionary<string, EntityMetadata> EntityPermissionsMap { get; }

        /// <summary>
        /// Checks for the existence of the client role header in httpContext.Request.Headers
        /// and evaluates that header against the authenticated (httpContext.User)'s roles
        /// </summary>
        /// <param name="httpContext">Contains request headers and metadata of the authenticated user.</param>
        /// <returns>True, if client role header exists and matches authenticated user's roles.</returns>
        public bool IsValidRoleContext(HttpContext httpContext);

        /// <summary>
        /// Checks if the permissions collection of the requested entity
        /// contains an entry for the role defined in the client role header.
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="operation">Operation type: Create, Read, Update, Delete</param>
        /// <returns>True, if a matching permission entry is found.</returns>
        public bool AreRoleAndOperationDefinedForEntity(string entityName, string roleName, Operation operation);

        /// <summary>
        /// Any columns referenced in a request's headers, URL(filter/orderby/routes), and/or body
        /// are compared against the inclued/excluded column permission defined for the entityName->roleName->operation
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="operation">Operation type: Create, Read, Update, Delete</param>
        /// <param name="columns">Compiled list of any column referenced in a request</param>
        /// <returns></returns>
        public bool AreColumnsAllowedForOperation(string entityName, string roleName, Operation operation, IEnumerable<string> columns);

        /// <summary>
        /// Method to return the list of exposed columns for the given combination of
        /// entityName, roleName, operation.
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="operation">Operation type: Create, Read, Update, Delete</param>
        /// <returns></returns>
        public IEnumerable<string> GetAllowedExposedColumns(string entityName, string roleName, Operation operation);

        /// <summary>
        /// Retrieves the policy of an operation within an entity's role entry
        /// within the permissions section of the runtime config, and tries to process
        /// the policy.
        /// </summary>
        /// <param name="entityName">Entity from request.</param>
        /// <param name="roleName">Role defined in client role header.</param>
        /// <param name="operation">Operation type: Create, Read, Update, Delete.</param>
        /// <param name="httpContext">Contains token claims of the authenticated user used in policy evaluation.</param>
        /// <returns>Returns the parsed policy, if successfully processed, or an exception otherwise.</returns>
        public string ProcessDBPolicy(string entityName, string roleName, Operation operation, HttpContext httpContext);

        /// <summary>
        /// Get list of roles defined for entity within runtime configuration.. This is applicable for GraphQL when creating authorization
        /// directive on Object type.
        /// </summary>
        /// <param name="entityName">Name of entity.</param>
        /// <returns>Collection of role names.</returns>
        public IEnumerable<string> GetRolesForEntity(string entityName);

        /// <summary>
        /// Returns the collection of roles which can perform {operation} the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName">EntityName whose operationMetadata will be searched.</param>
        /// <param name="field">Field to lookup operation permissions</param>
        /// <param name="operation">Specific operation to get collection of roles</param>
        /// <returns>Collection of role names allowed to perform operation on Entity's field.</returns>
        public IEnumerable<string> GetRolesForField(string entityName, string field, Operation operation);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="roleName"></param>
        /// <param name="httpVerb"></param>
        /// <returns>True if the execution of the stored procedure is permitted.</returns>
        public bool IsStoredProcedureExecutionPermitted(string entityName, string roleName, string httpVerb);

        /// <summary>
        /// Returns a list of roles which define permissions for the provided operation.
        /// i.e. list of roles which allow the operation 'Read' on entityName.
        /// </summary>
        /// <param name="entityName">Entity to lookup permissions</param>
        /// <param name="operation">Operation to lookup applicable roles</param>
        /// <returns>Collection of roles. Empty list if entityPermissionsMap is null.</returns>
        public static IEnumerable<string> GetRolesForOperation(
            string entityName,
            Operation operation,
            Dictionary<string, EntityMetadata>? entityPermissionsMap)
        {
            if (entityName is null)
            {
                throw new ArgumentNullException(paramName: "entityName");
            }

            if (entityPermissionsMap is not null &&
                entityPermissionsMap[entityName].OperationToRolesMap.TryGetValue(operation, out List<string>? roleList) &&
                roleList is not null)
            {
                return roleList;
            }

            return new List<string>();
        }
    }
}
