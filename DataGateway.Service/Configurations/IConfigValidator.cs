namespace Azure.DataGateway.Service.Configurations
{

    /// <summary>
    /// Validates the application logic config
    /// </summary>
    public interface IConfigValidator
    {
        /// <summary>
        /// Validate the application logic of the resolved config both within the
        /// config itself and in relation to the graphQL schema
        /// </summary>
        void ValidateConfig();
    }
}
