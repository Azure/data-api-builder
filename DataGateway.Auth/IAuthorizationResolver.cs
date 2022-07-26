using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Auth
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
        /// <param name="action">Action type: Create, Read, Update, Delete</param>
        /// <returns>True, if a matching permission entry is found.</returns>
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, string action);

        /// <summary>
        /// Any columns referenced in a request's headers, URL(filter/orderby/routes), and/or body
        /// are compared against the inclued/excluded column permission defined for the entityName->roleName->action
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="action">Action type: Create, Read, Update, Delete</param>
        /// <param name="columns">Compiled list of any column referenced in a request</param>
        /// <returns></returns>
        public bool AreColumnsAllowedForAction(string entityName, string roleName, string action, IEnumerable<string> columns);

        /// <summary>
        /// From the given parameters, processes the included and excluded column permissions to output
        /// a list of columns that are "allowed".
        /// -- IncludedColumns minus ExcludedColumns == Allowed Columns
        /// -- Does not yet account for either being wildcard (*).
        /// </summary>
        /// <param name="allowedExposedColumns">Set of fields exposed to user.</param>
        /// <param name="entityName">Entity from request</param>
        /// <param name="allowedDBColumns">Set of allowed backing field names.</param>
        public void PopulateAllowedExposedColumns(HashSet<string> allowedExposedColumns, string entityName, HashSet<string> allowedDBColumns);

        /// <summary>
        /// Method to return the list of exposed columns for the given combination of
        /// entityName, roleName, action.
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="actionName">Action type: Create, Read, Update, Delete</param>
        /// <returns>List of allowed columns</returns>
        public IEnumerable<string> GetAllowedColumns(string entityName, string roleName, string actionName);

        /// <summary>
        /// Retrieves the policy of an action within an entity's role entry
        /// within the permissions section of the runtime config, and tries to process
        /// the policy.
        /// </summary>
        /// <param name="entityName">Entity from request.</param>
        /// <param name="roleName">Role defined in client role header.</param>
        /// <param name="action">Action type: Create, Read, Update, Delete.</param>
        /// <param name="httpContext">Contains token claims of the authenticated user used in policy evaluation.</param>
        /// <returns>Returns the parsed policy, if successfully processed, or an exception otherwise.</returns>
        public string TryProcessDBPolicy(string entityName, string roleName, string action, HttpContext httpContext);

        /// <summary>
        /// Get list of roles defined for entity within runtime configuration.. This is applicable for GraphQL when creating authorization
        /// directive on Object type.
        /// </summary>
        /// <param name="entityName">Name of entity.</param>
        /// <returns>Collection of role names.</returns>
        public IEnumerable<string> GetRolesForEntity(string entityName);

        /// <summary>
        /// Returns the collection of roles which can perform {actionName} the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName">EntityName whose actionMetadata will be searched.</param>
        /// <param name="field">Field to lookup action permissions</param>
        /// <param name="actionName">Specific action to get collection of roles</param>
        /// <returns>Collection of role names allowed to perform actionName on Entity's field.</returns>
        public IEnumerable<string> GetRolesForField(string entityName, string field, string actionName);

        /// <summary>
        /// Returns a list of roles which define permissions for the provided action.
        /// i.e. list of roles which allow the action "read" on entityName.
        /// </summary>
        /// <param name="entityName">Entity to lookup permissions</param>
        /// <param name="actionName">Action to lookup applicable roles</param>
        /// <returns>Collection of roles. Empty list if entityPermissionsMap is null.</returns>
        public static IEnumerable<string> GetRolesForAction(
            string entityName,
            string actionName,
            Dictionary<string, EntityMetadata>? entityPermissionsMap)
        {
            if (entityName is null)
            {
                throw new ArgumentNullException(paramName: "entityName");
            }

            if (entityPermissionsMap is not null &&
                entityPermissionsMap[entityName].ActionToRolesMap.TryGetValue(actionName, out List<string>? roleList) &&
                roleList is not null)
            {
                return roleList;
            }

            return new List<string>();
        }
    }
}
