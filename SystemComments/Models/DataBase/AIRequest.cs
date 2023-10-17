using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SystemComments.Models.DataBase
{
    public class AIRequest
    {
        public string InputPrompt { get; set; }
        public string Output { get; set; }

        public Int16 AttemptNumber { get; set; }

        public Int64 UserID { get; set; }

        public string DateRange { get; set; }

        public Int64 CreatedBy { get; set; }

        public Int64 DepartmentID { get; set; }

        public string SearchCriteria { get; set; }
    }
}
