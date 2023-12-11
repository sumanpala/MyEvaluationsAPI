using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SystemComments.Models.DataBase
{
    public class TaskRequest
    {
        public string DepartmentName { get; set; }

        public string StartDate { get; set; }

        public string EndDate { get; set; }

        public Int32 StartIndex { get; set; } = 1;

        public Int32 PageSize { get; set; } = 1000;

        public string EDIPI { get; set; }

        public string Status { get; set; }
    }
}
