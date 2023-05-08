using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    }
}
