using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SystemComments.Models.DataBase
{
    public class Comments
    {
        public string InputComments { get; set; }
    }

    public class PromptInfoModel
    {
        public string Prompt { get; set; }

        public string EvaluatorFullName { get; set; }

        public string StartDate { get; set; }

        public string EndDate { get; set; }

        public string Role { get; set; }

        public string DateRange { get; set; }

        public Int32 ReviewedEvaluations { get; set; }
    }

    public class TemplateModel
    {
        public long TemplateID { get; set; }

        public string TemplateName { get; set; }

        public string ProgramName { get; set; }
    }

    public class TemplateUserModel
    {
        public long TemplateID { get; set; }

        public long SubjectUserID { get; set; }

        public string UserFullName { get; set; }
    }

    public class QuestionCommentModel
    {
        public long EvaluationID { get; set; }

        public long TemplateID { get; set; }

        public long SubjectUserID { get; set; }

        public long QuestionID { get; set; }

        public string RotationName { get; set; }

        public string Question { get; set; }

        public string Comments { get; set; }

        public string EWComments { get; set; }

        public string EEComments { get; set; }

        public string EPAEWComments { get; set; }

        public string EPAEEComments { get; set; }

        public string EPAComments { get; set; }

        public string EvaluationPeriod { get; set; }
    }

    public class EvaluatorCommentModel
    {
        public long EvaluationID { get; set; }

        public long TemplateID { get; set; }

        public long SubjectUserID { get; set; }

        public string EvaluatorComments { get; set; }

        public string ReviewComments { get; set; }

        public string RotationName { get; set; }

        public string EvaluationPeriod { get; set; }
    }

    public class SageCommentModel
    {
        public long EvaluationID { get; set; }

        public long TemplateID { get; set; }

        public long SubjectUserID { get; set; }

        public string QuestionDescription { get; set; }

        public string HeaderType { get; set; }

        public string SectionName { get; set; }

        public int SectionNumber { get; set; }

        public string Answer { get; set; }

        public long QuestionID { get; set; }

        public long? MainQuestionID { get; set; }
    }
}
