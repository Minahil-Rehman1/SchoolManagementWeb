using Microsoft.Data.SqlClient;

namespace SchoolManagementWeb.Models
{
    public class DatabaseHelper
    {
        private readonly string connectionString;

        public DatabaseHelper(IConfiguration configuration)
        {
            connectionString =
                configuration.GetConnectionString("SchoolDatabase")
                ?? throw new Exception("Database connection string not found.");
        }

        public SqlConnection GetConnection()
        {
            return new SqlConnection(connectionString);
        }
    }
}