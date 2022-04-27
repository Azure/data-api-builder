namespace Hawaii.Cli.Classes
{
    public class DataSource {
        public string database_type = "";
        public string connection_string = "";
    }
    
    public class Config {
        public DataSource data_source = new DataSource();
    }
}