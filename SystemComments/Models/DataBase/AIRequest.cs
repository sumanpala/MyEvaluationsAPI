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

        public string Feedback { get; set; }

        public Int16 RequestType { get; set; } = 0;

        public string AIResponseID { get; set; }

        public Int16 IsNPV { get; set; } = 0;

        public Int16 IsAPE { get; set; } = 0;

        public Int16 IsSage { get; set; } = 0;

        public string RotationName { get; set; }

        public string TrainingLevel { get; set; }

        public Int64 EvaluationID { get; set; }

        public string DepartmentName { get; set; }

        public string ActivityName { get; set; }

        public string Answer { get; set; }

        public string SageRequest { get; set; }

        public Int32 UserTypeID { get; set; } = 0;

        public string StartDate { get; set; }
        public string EndDate { get; set; }

        public string PromptWord { get; set; }

        public string PITPrompt { get; set; }
        public string AFIPrompt { get; set; }
        public string AFIProgramPrompt { get; set; }
        public Int32 AcademicYear { get; set; } = 0;
        public Int16 IsFaculty { get; set; } = 0;

    }
}
