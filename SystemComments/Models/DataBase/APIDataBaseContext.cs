using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using SystemComments.Models.DataBase;

namespace SystemComments.Models.DataBase
{
    public class APIDataBaseContext: DbContext
    {
        public APIDataBaseContext()
        {
        }

        public APIDataBaseContext(DbContextOptions<APIDataBaseContext> options)
            : base(options)
        {
        }

        public DbSet<SystemComments.Models.DataBase.AIResponse> AIResponse { get; set; }

        public DbSet<SystemComments.Models.DataBase.TaskResponse> TaskResponse { get; set; }

        //public DbConnection GetDbConnection()
        //{
        //    return Database.GetDbConnection(); // Works in EF Core
        //}

        public DataSet ExecuteStoredProcedure(string storedProcedureName, params SqlParameter[] parameters)
        {
            DataSet dataSet = new DataSet();

            // Manually retrieve the connection string from your DbContext
            string connectionString = this.Database.GetDbConnection().ConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Database connection string is not initialized.");
            }

            using (SqlConnection connection = new SqlConnection(connectionString)) // Explicitly create connection
            {
                try
                {
                    connection.Open(); // Ensure connection is open

                    using (SqlCommand cmd = new SqlCommand(storedProcedureName, connection))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandTimeout = 200;
                        if (parameters != null) cmd.Parameters.AddRange(parameters);

                        using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(dataSet); // Fill DataSet
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error executing stored procedure: {ex.Message}", ex);
                }
                finally
                {
                    connection.Close(); // Close connection
                }
            }

            return dataSet;
        }

    }
}
