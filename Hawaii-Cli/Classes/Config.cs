namespace Hawaii.Cli.Classes
{
    public class DataSource
    {
        public string database_type = "";
        public string connection_string = "";
    }

    public class Permission
    {
        public string role = "";
        public string actions = "";

        public Permission(string role, string actions)
        {
            this.role = role;
            this.actions = actions;
        }
    }

    public class Config
    {
        public DataSource data_source = new DataSource();
    }
}