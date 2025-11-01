using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SystemComments.Models.DataBase;

namespace SystemComments.Utilities
{
    public class BackEndService
    {
        public static async Task InsertAPEResponses(AIRequest input, APEResponse apeResponse,APIDataBaseContext _context)
        {
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@DepartmentID", input.DepartmentID),
                new SqlParameter("@UserID", input.UserID),
                new SqlParameter("@APEScheduleHistoryID", input.AIResponseID),
                new SqlParameter("@StartDate", input.StartDate),
                new SqlParameter("@EndDate", input.EndDate),
                new SqlParameter("@AcademicYear", input.AcademicYear),
                new SqlParameter("@AFIResponse", apeResponse.AFIJSON),
                new SqlParameter("@AFIProgramResponse", apeResponse.AFIProgramJSON),
                new SqlParameter("@PITResponse", apeResponse.PITJSON),
                new SqlParameter("@AFIPrompt", input.AFIPrompt),
                new SqlParameter("@AFIProgramPrompt", input.AFIProgramPrompt),
                new SqlParameter("@PITPrompt", input.PITPrompt)
            };
            await Task.Run(() => _context.ExecuteStoredProcedure("InsertAPEMyInsightsData", parameters));
        }

        public static async Task<string> GetAPEAreaOfImprovementsResponse(AIRequest input, APIDataBaseContext _context, IConfiguration _config)
        {
            string prompt = "";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@DepartmentID", input.DepartmentID),
                new SqlParameter("@UserID", input.UserID),
                new SqlParameter("@StartDate", input.StartDate),
                new SqlParameter("@EndDate", input.EndDate),
                new SqlParameter("@Word", input.PromptWord)
            };

            // Use Task.Run to ensure the method runs asynchronously
            await Task.Run(async () =>
            {
                DataSet dsInsights = _context.ExecuteStoredProcedure("ExtractMyInsights", parameters);
                if (dsInsights != null)
                {
                    DataTable dtPrompt = dsInsights.Tables[0];
                    DataTable dtInsights = dsInsights.Tables[1];
                    if (dtPrompt.Rows.Count > 0)
                    {
                        prompt = await SageExtraction.FormatHtml(dtPrompt.Rows[0]["FileContent"].ToString());
                        prompt = prompt.Replace("<br/>", "\n").Replace("</br>", "\n").Replace("<br>", "\n");
                        prompt = prompt.Replace("[Program Type]", dtPrompt.Rows[0]["ProgramName"].ToString());
                    }
                    if (dtInsights.Rows.Count > 0)
                    {
                        string result = string.Join(Environment.NewLine,
                            dtInsights.AsEnumerable()
                                .Select(row => row["AreasForImprovements"]?.ToString().Replace("\n", " ").Trim())
                                .Where(value => !string.IsNullOrEmpty(value))
                        );
                        result = Regex.Replace(result.Replace("\r\n", "\n").Replace("\n\n", "\n").Replace("<br/>", "\n"), "<.*?>", string.Empty);
                        result = await PromptService.SummarizeText(_config, result, 4000, 1);
                        //result = await SummarizeCommentsWithGPT(result);
                        prompt = prompt.Replace("[From uploaded file]", result);
                    }
                }
            });

            return prompt;
        }

        public static async Task<string> GetAPEAFIProgramResponse(AIRequest input, APIDataBaseContext _context, IConfiguration _config)
        {
            string prompt = "", pitPrompt = "";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@DepartmentID", input.DepartmentID),
                new SqlParameter("@StartDate", input.StartDate),
                new SqlParameter("@EndDate", input.EndDate)
            };

            // Use Task.Run to ensure the method runs asynchronously
            await Task.Run(async () =>
            {
                DataSet dsInsights = _context.ExecuteStoredProcedure("GetEvaluationProgramCommentsForMyInsights", parameters);
                if (dsInsights != null)
                {
                    DataTable dtPrompt = dsInsights.Tables[0];
                    DataTable dtPITPrompt = dsInsights.Tables[1];
                    DataTable dtInsights = dsInsights.Tables[2];
                    if (dtPrompt.Rows.Count > 0)
                    {
                        prompt = await SageExtraction.FormatHtml(dtPrompt.Rows[0]["FileContent"].ToString());
                        prompt = prompt.Replace("<br/>", "\n").Replace("</br>", "\n").Replace("<br>", "\n");
                        prompt = prompt.Replace("[Program Type]", dtPrompt.Rows[0]["ProgramName"].ToString());
                    }
                    if (dtPITPrompt.Rows.Count > 0)
                    {
                        pitPrompt = await SageExtraction.FormatHtml(dtPITPrompt.Rows[0]["FileContent"].ToString());
                        pitPrompt = pitPrompt.Replace("<br/>", "\n").Replace("</br>", "\n").Replace("<br>", "\n");
                        pitPrompt = pitPrompt.Replace("[Program Type]", dtPITPrompt.Rows[0]["ProgramName"].ToString());
                        input.PITPrompt = pitPrompt;
                    }
                    if (dtInsights.Rows.Count > 0)
                    {
                        string result = string.Join(Environment.NewLine,
                            dtInsights.AsEnumerable()
                                .Select(row => row["RotationName"] + "\n\n" + WebUtility.HtmlDecode(row["Comments"]?.ToString()).Trim().Replace("<br/>", "\n").Replace("\n\n", "\n").Replace("\n\n", "\n"))
                                .Where(value => !string.IsNullOrEmpty(value))
                        );

                        string rotations = string.Join(",",
                            dtInsights.AsEnumerable()
                            .Take(5)
                            .Select(row => "\"" + row["AbrRotationName"] + "\""));
                        result = Regex.Replace(result.Replace("\r\n", "\n").Replace("<br/>", "\n"), "<.*?>", string.Empty);
                        result = Regex.Replace(result, @"(\r?\n){2,}", "\n\n");

                        result = await PromptService.SummarizeText(_config, result, 9000, 2);
                        //result = await SummarizeCommentsWithGPT(result);
                        prompt = prompt.Replace("[From uploaded file]", result).Replace("[Rotations]", "[" + rotations + ",...]");
                    }
                }
            });

            return prompt;
        }

        public static string GetPreviousHistory(AIRequest input, string templateIDs, Int64 userID, APIDataBaseContext _context)
        {
            string userComments = string.Empty;
            SqlParameter[] parameters = new SqlParameter[]
                    {
                            new SqlParameter("@LoginDepartmentID", input.DepartmentID),
                            new SqlParameter("@LoginUserID", input.UserID),
                            new SqlParameter("@FromDate", DateTime.Now.AddYears(-1)),
                            new SqlParameter("@ToDate", DateTime.Now),
                            new SqlParameter("@SelectedUsers", userID.ToString()),
                            new SqlParameter("@SelectedTemplates", templateIDs),
                            new SqlParameter("@RotationID", "0"),
                            new SqlParameter("@EvaluatorID", "0"),
                            new SqlParameter("@InCludedCommnets", "Evaluation,EvaluateeAcknowledgement,EvaluatorAcknowledgement,ProgramDirector,FreeFormResponses,EarlyWarningComments,ExceedExpectationComments\r\n\t,ConfidentialComments,CMEEvaluations,ConferenceEvaluations,CAWComments,ProceduresComments,PatientLogsComments,LearningAssignmentsComments,MyPortfolio"),
                            new SqlParameter("@IsDeletedRotations","0")

                    };

            DataSet dsHistory = _context.ExecuteStoredProcedure("GetSystemComments", parameters);
            if (dsHistory.Tables.Count > 1)
            {
                DataTable dtComments = dsHistory.Tables[1];

                userComments += "\n Evaluation comments and feedback from various rotations and activities: \"\"\" \n";
                userComments += string.Format("\n Use the following narratives for {0} ", DateTime.Now.AddYears(-1).ToString("mm/dd/yyyy") + "-" + DateTime.Now.ToString("mm/dd/yyyy"));

                // Dictionary to map each CommentsType to relevant fields
                var commentsTypeFields = new Dictionary<string, List<string>>()
                {
                    { "1", new List<string> { "FreeFormComments", "QuestionComments", "EvaluationComments", "AdditionalComments", "ConfidentialComments", "AcknowledgedComments", "ReviewComments", "ProgramDirectorComments", "AdministratorComments", "GAComments", "PreceptorComments" } },
                    { "2", new List<string> { "EvaluationComments" } },
                    { "3", new List<string> { "EvaluationComments", "ConfidentialComments", "AdditionalComments" } },
                    { "4", new List<string> { "EvaluationComments", "ConfidentialComments" } },
                    { "5", new List<string> { "EvaluationComments" } },
                    { "6", new List<string> { "EvaluationComments" } },
                    { "7", new List<string> { "EvaluationComments" } },
                    { "8", new List<string> { "EvaluationComments" } }
                };

                // Use LINQ to filter rows and concatenate comments
                userComments += string.Join("\n",
                    dtComments.AsEnumerable()
                        .Where(dvr => commentsTypeFields.ContainsKey(dvr["CommentsType"].ToString()))
                        .SelectMany(dvr =>
                            commentsTypeFields[dvr["CommentsType"].ToString()]
                                .Where(field => dvr[field].ToString().Length > 0)
                                .Select(field => SageExtraction.RemoveUnNecessaryJSONTags(dvr[field].ToString()))
                        )
                );

            }

            return userComments;
        }

        public static void SaveSageResponse(APIDataBaseContext _context, string responseJSON, AIRequest input)
        {
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@EvaluationID", input.EvaluationID),
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@AIJSON", responseJSON)

                    };
            _context.ExecuteStoredProcedure("InsertSageResponse", parameters);
        }

        public static void UpdateAISageSettingsPrompt(APIDataBaseContext _context, Int64 id, string prompt)
        {
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@SageSettingsID", id),
                        new SqlParameter("@Prompt", prompt)

                    };
            _context.ExecuteStoredProcedure("UpdateAISageSettingsPrompt", parameters);
        }

        public static DataSet GetMyInsightsSummaryComments(APIDataBaseContext _context, AIRequest input)
        {
            DataSet dsResultSet = new DataSet();
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@AcademicYear", input.AcademicYear),
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@IsFaculty", input.IsFaculty)

                    };
            dsResultSet = _context.ExecuteStoredProcedure("GetMyInsightsSummaryComments", parameters);
            return dsResultSet;
        }

        public static DataSet InsertDepartmentalSummaryFromJson(APIDataBaseContext _context, AIRequest input, MyInsightsResponse sumamryResponse)
        {
            DataSet dsResultSet = new DataSet();
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@Academicyear", input.AcademicYear),
                        new SqlParameter("@IsFaculty", input.IsFaculty),
                        new SqlParameter("@Json", sumamryResponse.SummaryJSON)

                    };
            dsResultSet = _context.ExecuteStoredProcedure("usp_InsertDepartmentalSummaryFromJson", parameters);
            return dsResultSet;
        }

        public static DataSet SaveSageResponse(APIDataBaseContext _context,DataSet dsData, AIRequest input, string aiResponse, string aiPrompt, string extractJSON, TimeHistory timeHistory)
        {
            DataTable dtSections = new DataTable();
            DataTable dtSectionInfo = new DataTable();
            DataTable dtGuide = new DataTable();
            DataTable dtGuideQuestions = new DataTable();
            DataTable dtMainQuestions = new DataTable();
            DataTable dtFollowupSections = new DataTable();
            DataTable dtFollowupQuestions = new DataTable();
            string[] sectionColumns = { "ID", "name", "fullname", "sectionnum" };
            //string[] sectionInfoColumns = { "ID", "ParentID", "description", "wait" };
            string[] sectionMainQuestions = { "ID", "ParentID", "description", "mainquestion", "answer", "questionid", "wait" };
            string[] sectionGuide = { "ID", "ParentID", "description" };
            string[] sectionGuideQuestions = { "ID", "ParentID", "guidequestion", "questionid" };
            //string[] sectionFollowup = { "ID", "ParentID", "description" };
            string[] sectionFollowupQuestions = { "ID", "ParentID", "description", "question", "answer", "questionid", "wait" };
            string totalSections = "0";
            DataSet dsResultSet = new DataSet();
            if (dsData != null && dsData.Tables.Count > 0)
            {
                SageExtraction.ConvertColumnsToString(dsData);
                DataTable dtTotalCount = dsData.Tables[0];
                if (dtTotalCount.Rows.Count > 0 && dtTotalCount.Columns.Contains("Value"))
                {
                    totalSections = dtTotalCount.Rows[0]["Value"].ToString();
                }
                if (totalSections.Length == 0)
                {
                    totalSections = "0";
                }
                if (dsData.Tables.Count > 1)
                {
                    dtSections = dsData.Tables[1];
                }
                dtSections = SageExtraction.RemoveColumns(dtSections, sectionColumns);

                if (dsData.Tables.Count > 2)
                {
                    dtMainQuestions = dsData.Tables[2];
                    if (!dtMainQuestions.Columns.Contains("id"))
                    {
                        dtMainQuestions.Columns["id"].ColumnName = "questionid";
                    }
                }
                dtMainQuestions = SageExtraction.RemoveColumns(dtMainQuestions, sectionMainQuestions);

                if (dsData.Tables.Count > 3)
                {
                    dtGuide = dsData.Tables[3];
                }
                dtGuide = SageExtraction.RemoveColumns(dtGuide, sectionGuide);

                if (dsData.Tables.Count > 4)
                {
                    dtGuideQuestions = dsData.Tables[4];
                    if (!dtGuideQuestions.Columns.Contains("id"))
                    {
                        dtGuideQuestions.Columns["id"].ColumnName = "questionid";
                    }
                }
                dtGuideQuestions = SageExtraction.RemoveColumns(dtGuideQuestions, sectionGuideQuestions);

                //if (dsData.Tables.Count > 5)
                //{
                //    dtFollowupSections = dsData.Tables[5];
                //}
                //dtFollowupSections = SageExtraction.RemoveColumns(dtFollowupSections, sectionFollowup);
                if (dsData.Tables.Count > 5)
                {
                    dtFollowupQuestions = dsData.Tables[5];
                    if (!dtFollowupQuestions.Columns.Contains("id"))
                    {
                        dtFollowupQuestions.Columns["id"].ColumnName = "questionid";
                    }
                }
                dtFollowupQuestions = SageExtraction.RemoveColumns(dtFollowupQuestions, sectionFollowupQuestions);

                SqlParameter[] parameters = new SqlParameter[]
                    {
                            new SqlParameter("@DepartmentID", input.DepartmentID),
                            new SqlParameter("@UserID", input.UserID),
                            new SqlParameter("@EvaluationID", input.EvaluationID),
                            new SqlParameter("@TotalSections", totalSections),
                            new SqlParameter("@AIResponse", aiResponse),
                            new SqlParameter("@AIJSON", extractJSON),
                            new SqlParameter("@AIPrompt", aiPrompt),
                            new SqlParameter("@TotalSeconds", timeHistory.TotalSeconds),
                            new SqlParameter("@PromptSeconds", timeHistory.PromptDBSeconds),
                            new SqlParameter("@CommentsSeconds", timeHistory.HistorySeconds),
                            new SqlParameter("@AIResponseSeconds", timeHistory.AIResponseSeconds),
                            new SqlParameter("@TotalAttempts", timeHistory.ApiAttempts),
                            new SqlParameter("@tblSections", dtSections),
                            new SqlParameter("@tblMainQuestions", dtMainQuestions),
                            new SqlParameter("@tblGuide", dtGuide),
                            new SqlParameter("@tblGuideQuestions", dtGuideQuestions),
                            new SqlParameter("@tblFollowupQuestions", dtFollowupQuestions)

                    };

                dsResultSet = _context.ExecuteStoredProcedure("InsertSageEvaluation", parameters);

            }
            return dsResultSet;
        }
    }
}
