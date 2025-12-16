namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// The class is used for ephemeral feature flags to turn on/off features in development
    /// </summary>
    public class FeatureFlags
    {
        /// <summary>
        /// By default EnableDwNto1JoinQueryOptimization is disabled
        /// We should change the default as True once got more confidence with the fix
        /// </summary>
        public bool EnableDwNto1JoinQueryOptimization { get; set; }

        public FeatureFlags()
        {
            this.EnableDwNto1JoinQueryOptimization = false;
        }
    }
}
