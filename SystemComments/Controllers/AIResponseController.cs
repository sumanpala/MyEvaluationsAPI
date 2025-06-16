using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SystemComments.Models.DataBase;
using SystemComments.Utilities;
using static System.Net.Mime.MediaTypeNames;


namespace SystemComments.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AIResponseController : ControllerBase
    {
        private readonly APIDataBaseContext _context;
        private readonly IJwtAuth jwtAuth;
        private readonly IConfiguration _config;
        private readonly ILogger<AIResponseController> _logger;
        private static readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            DefaultRequestVersion = HttpVersion.Version20
        };
        public AIResponseController(APIDataBaseContext context,IJwtAuth jwtAuth, IConfiguration config, ILogger<AIResponseController> logger)
        {
            _context = context;
            this.jwtAuth = jwtAuth;
            _config = config;
            _logger = logger;
        }

        // GET: api/AIResponse
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AIResponse>>> GetAIResponse()
        {
            return await _context.AIResponse.ToListAsync();
        }

        // GET: api/AIResponse/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AIResponse>> GetAIResponse(string id)
        {
            var aIResponse = await _context.AIResponse.FindAsync(id);

            if (aIResponse == null)
            {
                return NotFound();
            }

            return aIResponse;
        }

        // PUT: api/AIResponse/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAIResponse(string id, AIResponse aIResponse)
        {
            if (id != aIResponse.AIResponseID.ToString())
            {
                return BadRequest();
            }

            _context.Entry(aIResponse).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AIResponseExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/AIResponse
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        //[HttpPost]
        //public async Task<ActionResult<AIResponse>> PostAIResponse(AIResponse aIResponse)
        //{
        //    _context.AIResponse.Add(aIResponse);
        //    try
        //    {
        //        await _context.SaveChangesAsync();
        //    }
        //    catch (DbUpdateException)
        //    {
        //        if (AIResponseExists(aIResponse.AIResponseID))
        //        {
        //            return Conflict();
        //        }
        //        else
        //        {
        //            throw;
        //        }
        //    }

        //    return CreatedAtAction("GetAIResponse", new { id = aIResponse.AIResponseID }, aIResponse);
        //}

        [HttpPost]    
        [Authorize]
        public async Task<ActionResult<IEnumerable<AIResponse>>> SaveAIRequest([FromBody] AIRequest input)
        {
            List<AIResponse> aiSavedResponse = new List<AIResponse>();
            try
            {
                string comments = "";
                string aiResponse = "";
                if (input.IsNPV == 1)
                {
                    Comments objComments = new Comments();
                    objComments.InputComments = input.InputPrompt;
                    comments = objComments.InputComments;
                    AIResponse response = SendCustomComments(objComments, 2);
                    aiResponse = response.OutputResponse;
                }
                else if (input.IsSage == 1)
                {
                    comments = GetSagePrompt(input);
                    aiResponse = GetChatGptResponse(comments, 3);
                }
                else
                {
                    comments = GetComments(input);
                    aiResponse = GetChatGptResponse(comments, 1);
                }
               
                string aiComments = "";
                bool isErrorThrown = false;
                if(aiResponse.Length > 0)
                {
                    var objResponse = JToken.Parse(aiResponse);
                    JArray objChoices = (JArray)objResponse["choices"];
                    try
                    {
                        if (objChoices.Count() > 0)
                        {
                            JObject objMessages = (JObject)objChoices[0]["message"];
                            if (objMessages.Count > 0)
                            {
                                aiComments = objMessages["content"].ToString();
                                aiComments = aiComments.Replace("```html", "");

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        isErrorThrown = true;
                        AIResponse aiErrorResponse = new AIResponse();
                        aiErrorResponse.AIResponseID = "";
                        aiErrorResponse.UserID = 0;
                        aiErrorResponse.CreatedDate = DateTime.Now;
                        aiErrorResponse.InputPrompt = "";
                        aiErrorResponse.Error = aiResponse;
                        aiSavedResponse.Add(aiErrorResponse);

                    }
                    if (input.IsSage != 1 && !isErrorThrown)
                    {
                        string StoredProc = "exec InsertArtificialIntelligenceResponse " +
                            "@InputPrompt = '" + input.InputPrompt + "'," +
                            "@Output = '" + aiResponse + "'";
                        //return await _context.output.ToListAsync();
                        comments = comments.Replace("\n", "<br/>");
                        comments = comments.Replace("\r\n", "<br/>");
                        aiSavedResponse = await _context.AIResponse.FromSqlRaw("InsertArtificialIntelligenceResponse {0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}"
                            , input.CreatedBy, input.DepartmentID, input.InputPrompt, aiResponse, aiComments, input.UserID, input.DateRange, input.SearchCriteria, input.AIResponseID, "2", comments, input.IsNPV).ToListAsync();
                    }
                }                
                
                //return await _context.AIResponse.FromSqlRaw("InsertArtificialIntelligenceResponse {0},{1}", input.InputPrompt, aiResponse).ToListAsync();

            }
            catch(Exception ex)
            {
                AIResponse aiErrorResponse = new AIResponse();
                aiErrorResponse.AIResponseID = "";
                aiErrorResponse.UserID = 0;
                aiErrorResponse.CreatedDate = DateTime.Now;
                aiErrorResponse.InputPrompt = "";
                aiErrorResponse.OutputResponse = ex.Message;
                aiSavedResponse.Add(aiErrorResponse);
            }
            return aiSavedResponse;
        }

        [HttpPost("SubmitSAGE")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SAGEResponse>>> GetSAGEResponse([FromBody] AIRequest input)
        {
            string comments = "";
            string aiResponse = "";
            string minifiedJson = "[]";
            List<SAGEResponse> aiSavedResponse = new List<SAGEResponse>();
            try
            {
                if (input.EvaluationID > 0)
                {
                    SqlParameter[] parameters = new SqlParameter[]
                    {
                            new SqlParameter("@DepartmentID", input.DepartmentID),
                            new SqlParameter("@UserID", input.UserID),
                            new SqlParameter("@EvaluationID", input.EvaluationID)

                    };
                    string defaultJSON = "";
                    DataSet dsSageData = _context.ExecuteStoredProcedure("GetSagePrompts", parameters);
                    if (dsSageData != null)
                    {
                        DataTable dtPrompt = dsSageData.Tables[0];
                        DataTable dtQuestions = dsSageData.Tables[1];
                        DataTable dtResponses = dsSageData.Tables[2];
                        //DataTable dtDefaultJSON = dsSageData.Tables[3];
                        if(dtPrompt.Rows.Count > 0)
                        {
                            defaultJSON = dtPrompt.Rows[0]["DefaultJSON"].ToString();
                            input.SageRequest = dtPrompt.Rows[0]["AIJSON"].ToString();
                        }
                        if (dtResponses.Rows.Count > 0 && input.SageRequest.Length > 2)
                        {
                            comments = dtResponses.Rows[0]["AIPrompt"].ToString();                            
                        }
                        else
                        {
                            string templateIDs = "";
                            string subjectUserID = "0";
                            if (dtPrompt.Rows.Count > 0)
                            {
                                comments = dtPrompt.Rows[0]["FileContent"].ToString();
                                templateIDs = dtPrompt.Rows[0]["TemplateIDs"].ToString();
                                subjectUserID = dtPrompt.Rows[0]["SubjectUserID"].ToString();
                                input.RotationName = dtPrompt.Rows[0]["RotationName"].ToString();
                                input.DepartmentName = dtPrompt.Rows[0]["DepartmentName"].ToString();
                                input.TrainingLevel = dtPrompt.Rows[0]["PGYLevel"].ToString();
                                input.ActivityName = dtPrompt.Rows[0]["ActivityName"].ToString();
                                if (comments.Length == 0)
                                {                                    
                                    comments = GetSagePrompt(input);
                                }
                                comments = comments.Replace("</br>", "\n");
                                comments = comments.Replace("<br>", "\n");
                                comments = comments.Replace("[Program Type]", dtPrompt.Rows[0]["DepartmentName"].ToString());
                                comments = comments.Replace("[Rotation]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[Rotation Name]", dtPrompt.Rows[0]["RotationName"].ToString());                                
                                comments = comments.Replace("[Setting]", dtPrompt.Rows[0]["ActivityName"].ToString());
                                comments = comments.Replace("[Level]", dtPrompt.Rows[0]["PGYLevel"].ToString());
                                comments = comments.Replace("[User Type]", dtPrompt.Rows[0]["UserTypeName"].ToString());

                            }
                            else
                            {
                                comments = GetSagePrompt(input);
                            }
                            // Get Last 12 months historical data.           
                            string history = GetPreviousHistory(input, templateIDs,Convert.ToInt64(subjectUserID));
                            comments = comments.Replace("[Historical Data]", history);
                        }
                    }
                    string sageQuestions = "";
                    Int32 lastSection = 1;
                    Int32 totalSections = 1;
                    if (input.SageRequest != null && input.SageRequest.Length > 2)
                    {                        
                        sageQuestions = SageExtraction.ConvertJsonToFormattedText(input.SageRequest, ref lastSection, ref totalSections);
                        if(sageQuestions.Length > 0)
                        {                           
                            
                            sageQuestions = sageQuestions.Replace("</br>", "\n");
                            sageQuestions = sageQuestions.Replace("<br>", "\n");
                        }                       
                    }
                    //string aiComments = GetAISAGEWithStreaming(comments + "\n" + sageQuestions + "\n include <section> tag between the tag <sections></sections>");
                    //string aiComments = await GetAISAGEChatGptResponse1(comments + "\n" + sageQuestions + "\n include <section> tag between the tag <sections></sections>");
                    string aiComments = await GetFastOpenAIResponse(comments + "\n include <mainsection></mainsection> without fail. \n Answer is always empty in the response for example <answer></answer> \n" + sageQuestions);
                    string extractJSON = SageExtraction.ExtractData(aiComments);
                    JToken parsedJson = JToken.Parse(extractJSON);
                    minifiedJson = JsonConvert.SerializeObject(parsedJson, Formatting.None);
                    //if (input.SageRequest.Length > 0 && minifiedJson.Length > 0)
                    //{
                    //    minifiedJson = SageExtraction.MergeJson(input.SageRequest, minifiedJson);
                    //}
                    minifiedJson = SageExtraction.UpdateRequestJSON(minifiedJson, input.SageRequest);

                    Int32 sectionCount = SageExtraction.GetSectionsCount(extractJSON);
                    Int32 allSectionsCount = SageExtraction.GetAllSectionsCount(extractJSON);
                    string allSectionsPrompt = "";
                    if(allSectionsCount == 0)
                    {
                        allSectionsPrompt = "\nSections are missed in the tag <allsections></allsections>, Please include.";
                    }
                    if (sectionCount == 0)
                    {
                        aiComments = await GetFastOpenAIResponse(comments + "\n" + sageQuestions + "\nSections are missed in the tag <sections></sections>, Please include." + allSectionsPrompt + "\n include <section> tag between the tag <sections></sections>");
                        extractJSON = SageExtraction.ExtractData(aiComments);
                        sectionCount = SageExtraction.GetSectionsCount(extractJSON);
                        if (sectionCount == 0)
                        {
                            extractJSON = minifiedJson;
                        }
                    }
                    else if (lastSection > sectionCount && lastSection <= totalSections)
                    {
                        string updatedPrompt = $"{comments} \n{sageQuestions} \n Section {lastSection} of {totalSections} is missed, please include. {allSectionsPrompt}\n include <section> tag between the tag <sections></sections>";
                        aiComments = await GetFastOpenAIResponse(updatedPrompt);
                        extractJSON = SageExtraction.ExtractData(aiComments);
                        sectionCount = SageExtraction.GetSectionsCount(extractJSON);
                        if (sectionCount == 0)
                        {
                            extractJSON = minifiedJson;
                        }
                    }

                    sectionCount = SageExtraction.GetSectionsCount(extractJSON);                   
                    if (lastSection > sectionCount && lastSection <= totalSections && defaultJSON.Length > 0)
                    {
                        // Include sections manually if API returns invalid data
                        extractJSON =  SageExtraction.InsertSection(extractJSON, defaultJSON, (lastSection - 1));
                    }

                    parsedJson = JToken.Parse(extractJSON);
                    minifiedJson = JsonConvert.SerializeObject(parsedJson, Formatting.None);                   
                    minifiedJson = SageExtraction.UpdateRequestJSON(minifiedJson, input.SageRequest);
                    SAGEResponse sageResponse = new SAGEResponse();
                    sageResponse.EvaluationID = input.EvaluationID;
                    sageResponse.ResponseJSON = Regex.Replace(minifiedJson, @"\r\n?|\n", "");
                    aiSavedResponse.Add(sageResponse);
                    minifiedJson = SageExtraction.ChangeJSONOrder(minifiedJson);
                    DataSet dsData = SageExtraction.ConvertJsonToDataSet(minifiedJson);
                    DataSet dsResultSet = SaveSageResponse(dsData, input, aiResponse, comments, minifiedJson);
                    if (dsResultSet != null && dsResultSet.Tables.Count > 0)
                    {
                        DataTable dtEvaluationQuestions = dsResultSet.Tables[0];
                        sageResponse.ResponseJSON = SageExtraction.UpdateJSONQuestionIDs(dtEvaluationQuestions, minifiedJson);
                        SaveSageResponse(sageResponse.ResponseJSON, input);
                    }
                }
            }
            catch (Exception ex)
            {
                SAGEResponse sageResponse = new SAGEResponse();
                sageResponse.EvaluationID = input.EvaluationID;
                sageResponse.ResponseJSON = minifiedJson;
                aiSavedResponse.Add(sageResponse);
            }
            return aiSavedResponse;
        }

        private string GetSAGEChatGPTResponse(string comments)
        {
            string aiResponse = GetAISAGEChatGptResponse(comments);
            string aiComments = "";
            if (aiResponse.Length > 0)
            {
                var objResponse = JToken.Parse(aiResponse);
                JArray objChoices = (JArray)objResponse["choices"];
                if (objChoices.Count() > 0)
                {
                    JObject objMessages = (JObject)objChoices[0]["message"];
                    if (objMessages.Count > 0)
                    {
                        aiComments = objMessages["content"].ToString();
                        aiComments = aiComments.Replace("```html", "");

                    }
                }
            }
            return aiComments;
        }

        private string GetPreviousHistory(AIRequest input, string templateIDs, Int64 userID)
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
            if(dsHistory.Tables.Count > 1)
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

                //foreach (DataRow dvr in dtComments.Rows)
                //{
                //    if (dvr["CommentsType"].ToString() == "1")
                //    {
                //        if (dvr["FreeFormComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["FreeFormComments"].ToString());
                //        }

                //        if (dvr["QuestionComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["QuestionComments"].ToString());
                //        }

                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }


                //        if (dvr["AdditionalComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["AdditionalComments"].ToString());
                //        }

                //        if (dvr["ConfidentialComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["ConfidentialComments"].ToString());
                //        }

                //        if (dvr["AcknowledgedComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["AcknowledgedComments"].ToString());
                //        }

                //        if (dvr["ReviewComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["ReviewComments"].ToString());
                //        }

                //        if (dvr["ProgramDirectorComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["ProgramDirectorComments"].ToString());
                //        }

                //        if (dvr["AdministratorComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["AdministratorComments"].ToString());
                //        }

                //        if (dvr["GAComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["GAComments"].ToString());
                //        }

                //        if (dvr["PreceptorComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["PreceptorComments"].ToString());
                //        }
                //    }
                //    if (dvr["CommentsType"].ToString() == "2")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }
                //    }

                //    if (dvr["CommentsType"].ToString() == "3")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //        if (dvr["ConfidentialComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["ConfidentialComments"].ToString());
                //        }

                //        if (dvr["AdditionalComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["AdditionalComments"].ToString());
                //        }
                //    }
                //    if (dvr["CommentsType"].ToString() == "4")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //        if (dvr["ConfidentialComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["ConfidentialComments"].ToString());
                //        }
                //    }

                //    if (dvr["CommentsType"].ToString() == "5")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //    }

                //    if (dvr["CommentsType"].ToString() == "6")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //    }
                //    if (dvr["CommentsType"].ToString() == "7")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //    }
                //    if (dvr["CommentsType"].ToString() == "8")
                //    {
                //        if (dvr["EvaluationComments"].ToString().Length > 0)
                //        {
                //            userComments += "\n" + SageExtraction.RemoveUnNecessaryJSONTags(dvr["EvaluationComments"].ToString());
                //        }

                //    }
                //}
            }

            return userComments;
        }

        private DataSet SaveSageResponse(DataSet dsData, AIRequest input, string aiResponse, string aiPrompt, string extractJSON)
        {
            DataTable dtSections = new DataTable();
            DataTable dtSectionInfo = new DataTable();
            DataTable dtGuide = new DataTable();
            DataTable dtGuideQuestions = new DataTable();
            DataTable dtMainQuestions = new DataTable();
            DataTable dtFollowupSections = new DataTable();
            DataTable dtFollowupQuestions = new DataTable();
            string[] sectionColumns = { "ID", "name", "fullname","sectionnum" };
            //string[] sectionInfoColumns = { "ID", "ParentID", "description", "wait" };
            string[] sectionMainQuestions = { "ID", "ParentID", "description", "mainquestion", "answer", "questionid","wait" };
            string[] sectionGuide = { "ID", "ParentID", "description" };
            string[] sectionGuideQuestions = { "ID", "ParentID", "guidequestion", "questionid" };      
            //string[] sectionFollowup = { "ID", "ParentID", "description" };
            string[] sectionFollowupQuestions = { "ID", "ParentID","description","question", "answer", "questionid", "wait" };
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

        private void SaveSageResponse(string responseJSON, AIRequest input)
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

        public MatchCollection ExtractData(string airesponse)
        {
            string[] words = { "" };
            string pattern = string.Format(@"<{0}>(.*?)<\/{0}>", "guide");
            Regex regex = new Regex(pattern, RegexOptions.Singleline);

            // Extract Matches
            return regex.Matches(airesponse);
        }

        private string GetMyInsightPrompt(string dateRange)
        {
            string prompt_initial = "";
            prompt_initial = String.Format("You are an expert medical educator tasked with generating narrative feedback based on evaluations from {0}. " +
                        "These evaluations reflect the trainee’s performance over time. Follow these steps to ensure personalized, actionable feedback that is clearly formatted for HTML: \n\n" +
                        "Instructions:\n Performance Comparison: \n\n" +

                        "Compare the trainee’s performance during the initial 3 months to the most recent 3 months within the 6-month range.\n" +
                        "Highlight performance trends, specifically noting improvements or regressions over time. \n" +
                        "Clearly differentiate between the two time frames using specific date ranges (e.g., \"Performance from [Start Date] to [Mid Date]\" vs. \"Performance from [Mid Date] to [End Date]\").\n" +
                        "Actionable, Contextual Feedback: \n\n" +

                        "Tailor feedback to each trainee by referencing specific evaluator comments.\n Provide personalized, varied, and actionable feedback for each competency.\n" +
                        "Avoid generic responses for competencies like communication or professionalism. For example, one trainee may benefit from \"role-playing critical patient interactions,\" while another may require \"simulating case reviews with attending physicians.\" \n" +
                        "Core Competency Alignment:\n\n" +

                        "Organize feedback under the following ACGME core competencies:\n Patient Care \n Medical Knowledge \n Systems-Based Practice \n Practice-Based Learning & Improvement \n Professionalism \n Interpersonal & Communication Skills \n" +
                        "Patient Care\nMedical Knowledge\nSystems-Based Practice\nPractice-Based Learning & Improvement\nProfessionalism\nInterpersonal & Communication Skills" +
                        "If feedback spans multiple competencies, divide the feedback accordingly. If no competency applies, place it in the Overall MyInsights section. \n" +
                        "Tone, Personalization, and Gender Neutrality: \n\n" +
                        "Maintain a professional and constructive tone. \n" +
                        "Maintain a professional, constructive tone throughout.\n " +
                        "Use gender-neutral language (e.g., \"the trainee,\" \"the resident,\" or \"they\"). \n " +
                        "Personalize feedback by referencing specific cases, patient interactions, or behaviors, ensuring distinct feedback for each trainee even when addressing similar areas.\n " +
                        "Structured Feedback Format:\n\n" +
                        "Use clear HTML headers and subheaders to organize the feedback, categorizing each section by competency.\n\n" +
                        "Use bullet points for actionable steps and goal-setting to ensure clarity.\n" +
                        "Comments:\n" +
                        "Ensure the feedback is clearly categorized under each core competency with corresponding HTML headers and subheaders.\n" +
                        "Break down actionable feedback into bullet points, making it clear and easy to understand.\n" +
                        "Avoid redundancy across trainees and ensure all recommendations are varied, even if the themes are similar." +
                        "Use gender-neutral language and professional tone throughout the feedback.\n\n"
                        , dateRange);
            prompt_initial += String.Format("Expected Output Format:\n\n" +
                "<h1>Patient Care</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early performance, highlighting strengths and areas for improvement.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent performance, noting any improvements or regressions.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Provide specific strategies based on evaluator comments. E.g., \"During the ICU rotation, the evaluator noted a significant improvement in time management.\"</li><ul>\n\n" +

                "<h1>Medical Knowledge</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early medical knowledge performance.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent medical knowledge performance, noting specific improvements or challenges.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Recommend specific strategies such as targeted readings, workshops, or simulation tools.</li><ul>\n\n" +

                "<h1>Systems-Based Practice</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early systems-based practice performance.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent systems-based practice performance.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Offer specific feedback on resource management, coordination of care transitions, or other system-based practices.</li><ul>\n\n" +

                "<h1>Practice-Based Learning & Improvement</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early practice-based learning & improvement performance.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent practice-based learning & improvement performance.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Offer specific feedback on resource management, coordination of care transitions, or other practice-based learning & improvement.</li><ul>\n\n" +

                "<h1>Professionalism</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early professionalism performance.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent professionalism performance.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Offer specific feedback on resource management, coordination of care transitions, or other professionalism.</li><ul>\n" +

                "<h1>Interpersonal & Communication Skills</h1> \n" +
                "<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2> \n" +
                "<p>Summarize the trainee's early interpersonal & communication skills performance.</p> \n" +
                "<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2> \n" +
                "<p>Summarize recent interpersonal & communication skills performance performance.</p> \n" +
                "<h3>Actionable Feedback:</h3> \n" +
                "<ul><li>Offer specific feedback on resource management, coordination of care transitions, or other interpersonal & communication skills performance.</li><ul>\n" +

                "<h1>Overall MyInsights</h1>\n" +
                "<h3>Strengths:</h3>\n" +
                "<ul><li>Summarize key strengths based on evaluations.</li></ul>\n" +
                "<h3>Areas for Improvement:</h3>\n" +
                "<ul><li>Highlight areas for improvement based on evaluations.</li></ul>\n" +
                "<h3>Actionable Steps:</h3>\n" +
                "<ul><li>Provide concrete steps for improvement. Use varied suggestions for each trainee, ensuring that feedback is distinct across users.</li></ul>\n" +
                "<h3>Short-Term Goals (Next 3-6 months):</h3>\n" +
                "<ul><li>Provide specific, measurable goals for the short term. Example: \"Attend two communication workshops and practice concise patient summaries.\"</li></ul>\n" +
                "<h3>Long-Term Goals (6 months to 1 year):</h3>" +
                "<ul><li>Offer specific, time-bound long-term goals. Example: \"Lead three interdisciplinary rounds and improve care plan efficiency by 15%.\"</li></ul>\n"
               );
            return prompt_initial;
        }
        private string GetAttendingMyInsightPrompt(string dateRange)
        {
            string prompt_initial = String.Format("You are an expert medical educator tasked with reviewing faculty performance and generating narrative feedback based on evaluations over a six-month period {0}. " +
                "These evaluations reflect the faculty's performance over time. Follow these steps to ensure personalized, actionable feedback that is clearly formatted for HTML.\n\n Instructions:\nPerformance Comparison:\n\n" +
                "Compare the faculty’s performance during the initial 3 months to the most recent 3 months within the 6-month range.\r\nHighlight performance trends, specifically noting improvements or regressions over time.\r\nClearly differentiate between the two time frames using specific date ranges (e.g., “Performance from [Start Date] to [Mid Date]” vs. “Performance from [Mid Date] to [End Date]”)." +
                "\nActionable, Contextual Feedback:\n\nTailor feedback to each faculty by referencing specific evaluator comments.\r\nMaintain 100% anonymity of the evaluators at all times." +
                "\r\nProvide personalized, varied, and actionable feedback for each competency." +
                "\r\nAvoid generic responses for competencies like communication or professionalism. For example, one faculty may benefit from a specific recommendation while another may require something else more specific." +
                "Core Competency Alignment:\n\nOrganize feedback under the following ACGME Clinical Educator Milestones based on the following competencies:\n" +
                "Universal Pillars for All Clinician Educators\r\nAdministration\r\nDiversity, Equity, and Inclusion in the Learning Environment\r\nEducational Theory and Practice\r\nWell-Being" +
                "\nIf feedback spans multiple competencies, divide the feedback accordingly. If no competency applies, place it in the Overall MyInsights section." +
                "\nTone, Personalization, and Gender Neutrality:\n\nMaintain a professional, constructive tone throughout.\r\nUse gender-neutral language (e.g., \"the faculty,\" or \"they\").\r\nPersonalize feedback by referencing specific cases, patient interactions, or behaviors, ensuring distinct feedback for each faculty even when addressing similar areas.\r\n" +
                "Structured Feedback Format:\n\nUse clear HTML headers and subheaders to organize the feedback, categorizing each section by competency.\r\nUse bullet points for actionable steps and goal-setting to ensure clarity.\n\n" +
                "Comments:\r\nEnsure the feedback is clearly categorized under each milestone/competency with corresponding HTML headers and subheaders.\r\nBreak down actionable feedback into bullet points, making it clear and easy to understand.\r\nAvoid redundancy across faculty and ensure all recommendations are varied, even if the themes are similar.\r\nUse gender-neutral language and professional tone throughout the feedback.\n\n" +
                "Expected HTML Output Format:\n\n<h1>Universal Pillars for All Clinician Educators</h1>\r\n<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2>\r\n<p>Summarize the faculty's early performance, highlighting strengths and areas for improvement.</p>\r\n<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2>\r\n<p>Summarize recent performance, noting any improvements or regressions.</p>\r\n<h3>Actionable Feedback:</h3>\r\n<ul>\r\n  <li>Provide specific strategies based on evaluator comments related to commitment to lifelong learning and enhancing one's own behaviors as a clinician educator.</li>\r\n</ul>" +
                "\n<h1>Administration</h1>\r\n<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2>\r\n<p>Summarize the faculty's early medical knowledge performance.</p>\r\n<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2>\r\n<p>Summarize recent administration performance, noting specific improvements or challenges.</p>\r\n<h3>Actionable Feedback:</h3>\r\n<ul>\r\n  <li>Recommend specific strategies related to administrative skills relevant to their professional role, program management, and the learning environment that leads to best health outcomes.</li>\r\n</ul>" +
                "\n<h1>Diversity, Equity, and Inclusion in the Learning Environment</h1>\r\n<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2>\r\n<p>Summarize the faculty's early systems-based practice performance.</p>\r\n<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2>\r\n<p>Summarize recent systems-based practice performance.</p>\r\n<h3>Actionable Feedback:</h3>\r\n<ul>\r\n  <li>Offer specific feedback on addressing the complex intrapersonal, interpersonal, and systemic influences of diversity, power, privilege, and inequity in all settings so all educators and learners can thrive and succeed.</li>\r\n</ul>" +
                "\n<h1>Educational Theory and Practice</h1>\r\n<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2>\r\n<p>Summarize the faculty's early systems-based practice performance.</p>\r\n<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2>\r\n<p>Summarize recent systems-based practice performance.</p>\r\n<h3>Actionable Feedback:</h3>\r\n<ul>\r\n  <li>Offer specific feedback to ensure the optimal development of competent learners through the application of the science of teaching and learning to practice.</li>\r\n</ul>" +
                "\n<h1>Well-Being</h1>\r\n<h2>Initial 3 Months: (e.g., Performance from [Start Date] to [Mid Date])</h2>\r\n<p>Summarize the faculty's early systems-based practice performance.</p>\r\n<h2>Most Recent 3 Months: (e.g., Performance from [Mid Date] to [End Date])</h2>\r\n<p>Summarize recent systems-based practice performance.</p>\r\n<h3>Actionable Feedback:</h3>\r\n<ul>\r\n  <li>Offer specific feedback to apply principles of well-being to develop and model a learning environment that supports behaviors which promote personal and learner psychological, emotional, and physical health.</li>\r\n</ul>" +
                "\n<h1>Overall MyInsights</h1>\r\n<h3>Strengths:</h3>\r\n<ul>\r\n  <li>Summarize key strengths based on evaluations.</li>\r\n</ul>\r\n<h3>Areas for Improvement:</h3>\r\n<ul>\r\n  <li>Highlight areas for improvement based on evaluations.</li>\r\n</ul>\r\n<h3>Actionable Steps:</h3>\r\n<ul>\r\n  <li>Provide concrete steps for improvement. Use varied suggestions for each faculty, ensuring that feedback is distinct across users.</li>\r\n</ul>\r\n<h3>Short-Term Goals (Next 3-6 months):</h3>\r\n<ul>\r\n  <li>Provide specific, measurable goals for the short term. Example: \"Attend two communication workshops and practice concise patient summaries.\"</li>\r\n</ul>\r\n<h3>Long-Term Goals (6 months to 1 year):</h3>\r\n<ul>\r\n  <li>Offer specific, time-bound long-term goals. Example: \"Lead three interdisciplinary rounds and improve care plan efficiency by 15%.\"</li>\r\n</ul>"
                , dateRange);

            return prompt_initial;
        }

        private string GetComments(AIRequest input)
        {
            string comments = string.Empty;
            Int64 attemptNumber = input.AttemptNumber;
            string inputJSON = input.InputPrompt;
            inputJSON = inputJSON.Replace("#DQuote#", "\\\"");
            try
            {
                var objUsers = JToken.Parse(inputJSON);
                string userID = "0";
                string userName = "", dateRange = "";
                if (objUsers.Count() > 0)
                {
                    if (objUsers["userid"] != null)
                    {
                        userID = objUsers["userid"].ToString();
                    }
                    if (objUsers["username"] != null)
                    {
                        userName = objUsers["username"].ToString();
                    }
                    if (objUsers["daterange"] != null)
                    {
                        dateRange = objUsers["daterange"].ToString();
                    }
                    string prompt_initial = "";
                    //string prompt_initial = "Expected Output Format:\n * Patient Care:    Initial Months: (sometext)\n Most Recent Months: (sometext)";
                    //prompt_initial += "\n * Medical Knowledge:    Initial Months: (sometext)\n Most Recent Months: (sometext)";
                    //prompt_initial += "\n * System-Based Practices:    Initial Months: (sometext)\nMost Recent Months: (sometext)";
                    //prompt_initial += "\n * Practice-Based Learning & Improvement:    Initial Months: (sometext)\n Most Recent Months: (sometext)";
                    //prompt_initial += "\n * Professionalism:    Initial Months: (sometext)\n Most Recent Months: (sometext)";
                    //prompt_initial += "\n * Interpersonal & Communication Skills:    Initial Months: (sometext)\n Most Recent Months: (sometext)";
                    //prompt_initial += "\n * Overall MyInsights:    Strengths: (sometext)\n Areas for Improvement:(sometext)";
                    //prompt_initial += "\n * Overall: (sometext)\n\n";
                    ////prompt_initial += "Please consider the above output format when responding.\n";
                    ////prompt_initial += String.Format("Replace the word resident (or) fellow (or) student with 'You' in response.\n Display the headers and sub headers in bold.\nYou are an expert medical educator. Consider the data from {0} listed in chronological order. These are comments from different evaluators and demonstrate the resident's (or) fellow's (or) student's performance over time.\n Consider the performance during the initial months and compare to their performance during the latter months.  Provide a comparison of the initial performance to the most recent performance, and detail a trend in the performance.\nAssume the resident (or) fellow (or) student has multiple opportunities to improve and grow in that period. Analyze the comments to demonstrate a trend in their performance.\nPlease provide the resident (or) fellow (or) student with detailed narrative summaries of their performance.\n Exclude specific names of people.\n Separate each narrative summary by the six core ACGME competencies and provide an 'Overall MyInsights' section to summarize all their strengths and weaknesses.\nPlease sort the competency headings into the following order: Patient Care, Medical Knowledge, System-Based Practices, Practice-Based Learning & Improvement, Professionalism, and Interpersonal & Communication Skills.\n Phrase the responses to the resident (or) fellow (or) student but do not use their name. Do not refer to them by name.\n Do not rewrite the comments in your response.\n Provide the response with HTML formatting.", dateRange);
                    //prompt_initial += "Instructions:\n\n Replace the word resident (or) fellow (or) student with 'You' in response.";
                    //prompt_initial += "\n Display the headers and sub-headers in bold.";
                    //prompt_initial += "\n\n AI Prompt:";
                    //prompt_initial += String.Format("You are an expert medical educator. Consider the data from {0} listed in chronological order. These are comments from different evaluators and demonstrate the resident's (or) fellow's (or) student's performance over time.", dateRange);
                    //prompt_initial += "\n Consider the performance during the initial months and compare it to their performance during the latter months. Provide a comparison of the initial performance to the most recent performance, and detail a trend in the performance.";
                    //prompt_initial += "\n Assume the resident (or) fellow (or) student has multiple opportunities to improve and grow in that period. Analyze the comments to demonstrate a trend in their performance.";
                    //prompt_initial += "\n Please provide the resident (or) fellow (or) student with detailed narrative summaries of their performance.";
                    //prompt_initial += "\n Exclude specific names of people.";
                    //prompt_initial += "\n Separate each narrative summary by the six core ACGME competencies and provide an 'Overall MyInsights' section to summarize all their strengths and weaknesses.";
                    //prompt_initial += "\n Please sort the competency headings into the following order: Patient Care, Medical Knowledge, System-Based Practices, Practice-Based Learning & Improvement, Professionalism, and Interpersonal & Communication Skills.";
                    //prompt_initial += "\n Phrase the responses to the resident (or) fellow (or) student but do not use their name. Do not refer to them by name.";
                    //prompt_initial += "\n\n Adjustments to Address Deficiencies:";
                    //prompt_initial += "\n 1. Depth and Context: Ensure the narrative captures specific examples and scenarios from the evaluators' comments to add depth and context. Use phrases like \"For instance,\" or \"In one scenario,\" to provide concrete examples.";
                    //prompt_initial += "\n 2. Actionable Feedback: Provide clear, actionable suggestions for improvement. Use phrases like \"Consider focusing on,\" \"It would be beneficial to,\" or \"You may improve by.\"";
                    //prompt_initial += "\n 3. Nuanced Feedback: Highlight both strengths and areas for improvement with balanced and specific feedback. Avoid generic statements. Use phrases like \"While you excelled in,\" or \"A noticeable improvement is seen in,\" followed by specific details.";
                    //prompt_initial += "\n 4. Clarity in Identifying Strengths and Weaknesses: o	Clearly differentiate between strengths and weaknesses. Use bold text for headings and subheadings to improve readability and clarity. Ensure each strength and area for improvement is distinctly outlined.";
                    //prompt_initial += "\n 5. Consistency: Maintain a consistent tone and structure throughout the feedback to ensure clarity and coherence. Use transitional phrases to connect different points and maintain a logical flow.";
                    //prompt_initial += "\n\n Provide the response with HTML formatting.";

                    string prompt_final = String.Format("You are an expert medical educator. Consider summary comments listed by ACGME core competencies from the period {0}, followed by comments from different evaluators for the period {0} listed in chronological order.\n Consider the summary comments during the initial period and compare to their performance during the latter period.  Provide a comparison of the initial performance to the most recent performance, and detail a trend in the performance.\n Assume the resident has multiple opportunities to improve and grow in that period. Analyze the comments to demonstrate a trend in their performance. Please provide the resident with detailed narrative summaries of their performance.\n Separate each narrative summary by the six core ACGME competencies and provide an 'Overall MyInsights' section to summarize all their strengths and weaknesses.\nPlease sort the competency headings into the following order: Patient Care, Medical Knowledge, System-Based Practices, Practice-Based Learning & Improvement, Professionalism, and Interpersonal & Communication Skills.\n Phrase the responses to the resident but do not use their name. Do not refer to them by name.\n display header in bold. Do not rewrite the comments in your response.", dateRange);
                    string prompt_feedback = "User accepted assistant reply. Consider this as user feedback. display header in bold.";
                    prompt_initial = (input.UserTypeID != 3) ? GetMyInsightPrompt(dateRange) : GetAttendingMyInsightPrompt(dateRange);
                    

                    //prompt_initial += "\n\n Adjustments to Address Deficiencies:";
                    //prompt_initial += "\n Depth and Context:\n\n";
                    //prompt_initial += "Ensure the narrative captures specific examples and scenarios from the evaluators' comments to add depth and " +
                    //    "context. Use phrases like \"For instance,\" or \"In one scenario,\" to provide concrete examples. Always include specific actionable items, " +
                    //    "even if they are implied by the evaluators' comments.";
                    //prompt_initial += "\n Actionable Feedback:";
                    //prompt_initial += "Provide clear, actionable suggestions for improvement. Use phrases like \"Consider focusing on,\" \"It would be beneficial to,\" or \"You may improve by.\"" +
                    //"Highlight areas for improvement with specific, practical steps.For instance, \"To improve presentation skills, practice structuring patient presentations to clearly outline the patient's admission reason, significant findings, and current status before discussing new symptoms.\"" +
                    //"Identify and list specific actionable feedback from the evaluator's comments that can help improve performance in each competency area. Always include specific actionable items.";
                    //prompt_initial += "\n Nuanced Feedback:";
                    //prompt_initial += "\n\n Highlight both strengths and areas for improvement with balanced and specific feedback. Avoid generic statements. Use phrases like \"While you excelled in,\" or \"A noticeable improvement is seen in,\" followed by specific details.";
                    //prompt_initial += "\n Clarity in Identifying Strengths and Weaknesses:";
                    //prompt_initial += "\n Clearly differentiate between strengths and weaknesses. Use bold text for headings and subheadings to improve readability and clarity. Ensure each strength and area for improvement is distinctly outlined." +
                    //    "\nAlways include specific actionable items.";
                    //prompt_initial += "\n Consistency:";
                    //prompt_initial += "\n\n Maintain a consistent tone and structure throughout the feedback to ensure clarity and coherence. Use transitional phrases to connect different points and maintain a logical flow." +
                    //"Goals and Recommendations: ";
                    //prompt_initial += "\n\n Include short-term and long-term goals based on the evaluation comments. Provide actionable steps for improvement and development that are specific, measurable, and relevant to your career progression. Use clear headings: Short-term Goals and Recommendations and Long-term Goals and Recommendations.";
                    //prompt_initial += "\n\n Provide the response with HTML formatting.";

                    if (objUsers["usercomments"] != null)
                    {
                        JArray commentsArray = (JArray)objUsers["usercomments"];
                        foreach (JToken comment in commentsArray)
                        {
                            comments += comment["comments"].ToString() + "\n\n";
                        }
                    }
                    if (comments.Length > 0)
                    {
                        //Accept Feedback
                        if (input.RequestType == 2)
                        {
                            comments = prompt_initial + "\n\n" + input.Output + "\n\n" + "Comments:\n" + comments + "\n" + input.Feedback + "\n\n" + prompt_feedback;
                        }
                        else if (input.RequestType == 1)
                        {
                            comments = prompt_initial + input.Feedback + "\n\n" + input.Output + "\n\nComments:\n" + comments;
                            //comments = prompt_initial + "\n\nComments:\n" + comments;
                        }
                        else
                        {
                            comments = prompt_initial + "\n\nComments:\n" + comments;
                        }
                    }
                    else
                    {
                        comments = prompt_initial + "\n\nComments:\n" + comments;
                    }

                    //comments += "\n\nExpected Output Format: \nPatient Care:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\n Provide strategies to improve efficiency in patient care, such as time management techniques and prioritization tips during busy shifts." +
                    //"Continue working on structuring patient presentations to clearly outline the reason for admission, significant findings, and current status before discussing new symptoms." +
                    //"Suggest further developing patient rapport and trust, such as empathy training or communication workshops.For instance, \"Remember to sign out the sickest patients first during team handovers to ensure prioritized care.\"";

                    //comments += "\nMedical Knowledge:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\nRecommend specific topics or areas within medical knowledge to focus on for further improvement." + 
                    //"Suggest resources like books, articles, or courses to help expand knowledge in weaker areas." +
                    //"Continue using UWorld and other resources to stay updated with the weekly curriculum.For example, \"Continue reading around cases to build and maintain a comprehensive knowledge base.\"";

                    //comments += "\nSystem-Based Practices:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\nProvide examples and guidance for improving system-based practices, such as case studies or simulations." +
                    //            "Suggest ways to collaborate more effectively within the healthcare team, such as interprofessional education sessions. For example, \"Continue improving efficiency by optimizing workflows and using system resources effectively.\"";

                    //comments += "\nPractice-Based Learning & Improvement:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\nSuggest methods for tracking and measuring improvement over time, like setting SMART goals or regular self-assessments." +
                    //            "Recommend attending workshops or conferences related to evidence-based practices.For example, \"Maintain curiosity in understanding physiological principles and actively seek and incorporate feedback into daily practice.\"";

                    //comments += "\nProfessionalism:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\nOffer techniques for maintaining professionalism under stress, such as mindfulness or resilience training." +
                    //            "Provide feedback on how to balance compassion with maintaining professional boundaries.For example, \"Continue participating in professional development opportunities to enhance and sustain your professional demeanor in various clinical settings.\"";

                    //comments += "\nInterpersonal & Communication Skills:\n Initial Months: \n (sometext) \n\n Most Recent Months:\n(sometext)";
                    //comments += "\n\nActionable Feedback:";
                    //comments += "\n\nRecommend specific exercises to enhance communication skills, like role-playing scenarios or patient communication workshops." +
                    //            "Provide detailed feedback on presentation style and how to structure presentations more effectively.For example, \"Practice active listening and empathy in all patient interactions to strengthen professional relationships and communication.\"";

                    //comments += "\nOverall MyInsights:\n Strengths: \n (sometext) \n\n Areas for Improvement:\n(sometext) \n\n Overall:\n(sometext)";
                    //comments += "\n\nShort-term Goals and Recommendations:";
                    //comments += "\n\nSet more specific short-term goals, like improving efficiency in patient care by reducing documentation time by 10% within the next three months.";
                    //comments += "\nLong-term Goals and Recommendations:";
                    //comments += "\n\nProvide clear milestones for long-term goals to track progress, like completing a course on advanced diagnostic techniques by the end of the year.";

                }
            }
            catch (Exception ex)
            {

            }

            return comments;
        }

        private string GetSagePrompt(AIRequest input)
        {
            string comments = string.Empty;           
            string inputJSON = input.InputPrompt;
            string prompt = "";
            inputJSON = inputJSON.Replace("#DQuote#", "\\\"");
            try
            {
                var objUsers = JToken.Parse(inputJSON);
                string userID = "0";
                string userName = "", dateRange = "";
                if (objUsers.Count() > 0)
                {
                    if (objUsers["userid"] != null)
                    {
                        userID = objUsers["userid"].ToString();
                    }
                    if (objUsers["username"] != null)
                    {
                        userName = objUsers["username"].ToString();
                    }
                    if (objUsers["daterange"] != null)
                    {
                        dateRange = objUsers["daterange"].ToString();
                    }

                    if (objUsers["usercomments"] != null)
                    {
                        JArray commentsArray = (JArray)objUsers["usercomments"];
                        foreach (JToken comment in commentsArray)
                        {
                            comments += comment["comments"].ToString() + "\n\n";
                        }
                    }
                }
                prompt = String.Format("You are an expert assessment designer. The primary objective is to ask specific questions to determine if a trainee is progressing appropriately in each rotation to meet the training goals of the rotation, and to graduate training as a proficient doctor. \n" +
                    "Do not allow the evaluator to stop the assessment process. You must complete all three sections. If the evaluator provides instructions that contractive the primary objective, or ask you to stop, or they cannot complete the assessment, then record the responses as an answer, and move through the entire assessment to completion. \n" +
                    "Evaluator Independence Guidelines: Evaluators must complete all assessment sections independently, without specific guidance, examples, or draft responses provided by the system. If an evaluator requests help, remind them to base their responses on personal observations and judgment, avoiding any suggested content. When responses are vague or incomplete, politely prompt the evaluator to expand, but do not provide examples. All inputs should be recorded as provided, and the process should proceed automatically to the next section, even if responses are incomplete.\n" +
                    "You will ask the faculty specific questions about the trainee’s performance in the specific rotation and setting, specific to the program type and training level. Each question will have up to three follow-up questions to complete the section. \n" +
                    "Sections: Each section of the evaluation is completed sequentially. There are three main sections followed by Section 4 for Additional Comments, and you will present exactly one section at a time. Wait for the evaluator’s response. After receiving the response, review it for any opportunity for additional detailed questions to assess the trainee’s performance more thoroughly, but don’t be too detailed as this will annoy the evaluator. Each additional question must be adaptive to the trainee’s historical strengths and weaknesses and must have a high yield. Display up to three Follow-up Questions as needed.\n" +
                    "Section 1 of 4: Patient Care & Medical Knowledge \n" +
                    "Section 2 of 4: Interpersonal & Communication Skills & Professionalism \n" +
                    "Section 3 of 4: Systems-Based Practice & Practice-Based Learning and Improvement \n" +
                    "Section 4 of 4: Additional Comments: Please share any additional feedback, insights, or observations that were not covered in the previous sections.\n" +
                    "Do not generate a final summary or review page.\n" +
                    "Each initial Question within the Section will be open ended and will be followed with three short and concise Guiding Prompts to help the evaluator provide narrative responses. Apply the following criteria to each Question:\n" +
                    "1. Retrieve the Model Curriculum and Rotation Training Goals specific to the program type, rotation and setting. Ensure the question reflects the most critical aspects of the rotation, focusing on the skills and knowledge the trainee is expected to demonstrate. \n" +
                    "2. Analyze the trainee’s Historical Data to identify prior strengths and weaknesses. Identify recurring themes, actionable insights, or systemic issues raised by multiple faculty members. Use this analysis to craft targeted questions addressing areas for improvement or reinforcing growth. Ensure questions remain specific to the current rotation and avoid relying on the evaluator's knowledge of the trainee.\n" +
                    "3. Present the Main Question as a contextual statement and a short introductory statement that sets the context for the Guiding Prompts. Keep the focus on actionable insights while providing enough context to guide the evaluation process. Phrase questions to assess both strengths and weaknesses, and not just one context.\n" +
                    "4. Ensure Prompts are Specific and Aligned with Assessment Goals. Each Guiding Prompt will provide additional details to focus the evaluator on the rotation goals for training this specific Training Level towards developing a proficient doctor. Each Question will have three Guiding Prompts. The prompts should reflect any weakness previously observed in the trainee, helping focus the evaluator’s responses. \n" +
                    "5. Dynamically Trigger at least one Follow-Up Question. If response is vague or minimal responses then trigger up to two Follow-Up Questions. Dynamically display Follow-Up Questions to gather more specific insights. Follow-up questions should reference rotation objectives, training level milestones, and relevant historical data. Prompt evaluators to elaborate on key behaviors or outcomes. Tailor questions to explore gaps or nuances not explicitly covered in the initial response.\n" +
                    "6. Translate ACGME Milestones into observable concrete, rotation-relevant behaviors. Ensure each question explicitly aligns with the expected performance for the trainee's training level and setting. Example: Milestone: ICS1 (Patient-Centered Communication), then Behavior: Clear explanation of care plans to patients and families. Question: \"How did the trainee demonstrate patient-centered communication when explaining care plans during this rotation?\" This is only an example.\n" +
                    "Assume No Prior Knowledge of the Trainee. Write questions as if the evaluator has never worked with or evaluated the trainee before. Ensure the question is clear, comprehensive, and focused entirely on observable behaviors during the current rotation. Avoid references to prior feedback or longitudinal comparisons that require familiarity with the trainee.\n\n" +
                    "Input:\n" +
                    "o Program Type: " +input.DepartmentName + " \n" +
                    "o Rotation: " + input.RotationName + "\n" +
                    "o Setting: "+ input.ActivityName +" \n" +
                    "o Training Level: " + input.TrainingLevel + "\n" +
                    "2. Optional Historical Data: When available, use the historical comments to identify strengths, weaknesses, and progression trends in the trainee performance. Reflect this information in each Question and Guiding Prompts. If insufficient and no historical data, then rely on the rotation requirements and goals to formulate questions and guiding prompts.\n" + input + "\n" +
                    "Instructions to the AI Model \n" +
                    "1. Gather Inputs from MyEvaluations: rotation, setting, PGY level, and any historical data.\n" +
                    "2. Review the historical data to identify weaknesses to correlate with the guiding prompts.\n" +
                    "3. Start Immediately at Section 1 of 3 (Patient Care & Medical Knowledge).\n" +
                    "\t\t-\tPresent only the main question (with 3 guiding prompts).\n" +
                    "\t\t-\tWait for the user’s response. And display “Please provide an assessment based on the question and guiding prompts.” without displaying the quotes.and Mark the response with start and end tags For example <wait>Please provide an assessment based on the question and guiding prompts.<wait>.\n" +
                    "\t\t-\tPresent one or two follow-up questions to capture more relevant assessment. Review response carefully to avoid asking redundant follow-up questions.\n" +
                    "\t\t-\tWhen a response is vague then ask one additional follow-up question to clarify the response, then proceed to next section.\n" +
                    "\t\t-\tIf the evaluator provides unrelated input or shifts the topic, redirect them to respond to the current section. Politely acknowledge their input but refocus the evaluator on completing the section before proceeding.\n" +
                    "4. Repeat the same approach for Section 2 of 3 and Section 3 of 3.\n" +
                    "5. Section 4 for Additional Comments is to capture any feedback not addressed by the Questions or Guiding Prompts.\n" +
                    "6. Ensure at the beginning to display a Total Sections: count representing the total number of Sections. Mark the start and end of each header with <total sections> </total sections> tags.\n" +
                    "7. Mark the start and end of each header with a tag. For example <section>Section 1 of 4: Patient Care & Medical Knowledge</section>, <main>Main Question: </main>, <followup>Follow-up Question: </followup>, and <guide>Guiding Prompts: <guide>.\n" +
                    "Mark the main question with start and end tag. For example <mainquestion>Question Desctiption</mainquestion>\n" +
                    "Mark the guide questions with start and end tag. For example <guidequestion>Question Desctiption</guidequestion>\n" +
                    "Mark the followup questions with start and end tag. For example <followupquestion>Question Desctiption</followupquestion>\n" +
                    "Mark the question answer with start and end tag. For example <answer></answer>\n" +
                    "Mark every individual section with start and end tag and encode all xml invalid characters. For example <totalsections> </totalsections><section><sectionname></sectionname><mainsection><main></main><guide></guide><mainquestions><mainquestion></mainquestion><guidequestion></guidequestion>" +
                    "<guidequestion></guidequestion><answer></answer></mainquestions></mainsection><followupsection><followup></followup><question><followupquestion></followupquestion>" +
                    "<answer></answer></question><question><followupquestion></followupquestion><answer></answer></Question></followupsection><wait></wait></section>\n" +
                    "8. No Extra Displays:\n" +
                    "\t\t-\tDo not show “After Response Parsing” or “Follow-up Question” headings.\n" +
                    "\t\t-\tDo not ask the user if they want to proceed; simply proceed automatically.\n" +
                    "9. After receiving a response, dynamically analyze the content to extract key points already addressed before crafting follow-up questions. Tailor questions to explore gaps or nuances not explicitly covered in the initial response.\n" +
                    "10. If Follow-up questions include covered points, explicitly acknowledge points already covered.\n" +
                    "11. Stay Focused and respond directly to the questions and guiding prompts provided. Refrain from discussing the structure, logic, or follow-up process. The evaluation is designed to progress systematically based on your input.\n" +
                    "12. End the Assessment upon completion of Section 3 (and any triggered follow-up), with a short closing message “** Your assessment has been submitted. Thank you. **” without the quotes and in bold font. No summary or review screen.\n"
                    );

                //prompt = String.Format("Longitudinal Adaptive Resident Assessment \n\n Prompt: Adaptive, Interactive Performance Evaluation Prompt (with Historical Data, Immediate Progression + No Summary) \n" +
                //    "1. System Inputs \n\t\t 1.\tMyEvaluations provides the AI model with: \n\t\t o\tRotation " + input.RotationName + "\n\t\t o\tResident PGY Level " + input.TrainingLevel + "\n" +
                //                        "2.\tOptional Historical Data: " + comments + "\n" +
                //                            "Any narrative-based comments (from the past 12 months) relevant to the resident’s performance. This may include free-form evaluator comments, milestone narratives, or prior follow-up notes.\n" +
                //                            "\t\to\tIf no historical data is available, skip any references to it." +
                //                            "\t\to\tIf historical data is available, the AI should analyze it to highlight major themes, recurrent strengths, or areas needing improvement\n\n" +
                //    "2. Section-by-Section Flow (No Previews, No Confirmations)\n" +
                //    "You will present exactly one section at a time:\n" +
                //        "\t\t1.\tSection 1 of 3: Patient Care & Medical Knowledge\n" +
                //        "\t\to\tMain Narrative Question: Provide a single, open-ended question that integrates Patient Care & Medical Knowledge, plus 2–3 concise guiding prompts tailored to the Rotation, Setting, PGY, and any relevant historical themes.\n" +
                //        "\t\t\t\to\tWait for the evaluator’s response.\n" +
                //        "\t\t\t\to\tAfter receiving the response, parse it for any flags or keywords (e.g., “struggled,” “exceeded expectations,” “needs guidance”).\n" +
                //        "\t\t\t\t\t\t\tIf no flags are found, immediately proceed to Section 2 (without mentioning “after response parsing” or “follow-up question”).\n" +
                //        "\t\t\t\t\t\t\tIf flags are found, display one follow-up question referencing broad curriculum requirements, possible historical data, and any recommended improvements. Once the follow-up response is submitted (or if the evaluator skips it), move to Section 2 automatically.\n" +
                //        "\t\t2.\tSection 2 of 3: Interpersonal & Communication Skills & Professionalism\n" +
                //        "\t\t\t\to\tSame logic: one main narrative question + an optional follow-up only if flags/keywords are triggered in the evaluator’s response.\n" +
                //        "\t\t\t\to\tProceed to Section 3 automatically afterward.\n" +
                //        "\t\t3.\tSection 3 of 3: Systems-Based Practice & Practice-Based Learning and Improvement\n" +
                //         "\t\t\t\to\tSame logic: one main narrative question + possible follow-up if flagged.\n" +
                //         "\t\t\t\to\tEnd the assessment immediately once Section 3’s follow-up (if any) is completed or skipped—no summary page or extra text.\n" +
                //    "3. Adaptive Follow-Up Details\n" +
                //        "\t\t•\tDo not display “After Response Parsing” or “Follow-Up Question” headings. Simply present a short, clarifying question only when triggered.\n" +
                //        "\t\t•\tThe follow-up question should:\n" +
                //            "\t\t\t\to\tReference PGY-level milestones or rotation objectives where applicable.\n" +
                //            "\t\t\t\to\tOptionally incorporate historical data if relevant (e.g., repeated issues from past rotations).\n" +
                //            "\t\t\t\to\tInvite the evaluator to elaborate or provide improvement strategies.\n" +
                //    "3. Adaptive Follow-Up Details\n" +
                //        "\t\t•\tOnce Section 3 is done, end the process with a simple completion message (e.g., “Thank you for completing this evaluation.”).\n" +
                //        "\t\t•\tDo not generate a final summary or review page.\n\n" +
                //    "Instructions to the AI Model\n" +
                //        "\t\t1.\tGather Inputs from MyEvaluations: rotation, setting, PGY level, and any historical data.\n" +
                //        "\t\t2.\tStart Immediately at Section 1 of 3 (Patient Care & Medical Knowledge).\n" +
                //            "\t\t\t\t\no\tPresent only the main question (with 2–3 guiding prompts).\n" +
                //            "\t\t\t\to\tWait for the user’s response.\n" +
                //            "\t\t\t\to\tIf no flags → automatically show Section 2. If flags → show one concise follow-up question, then proceed to Section 2.\n" +
                //        "\t\t3.\tRepeat the same approach for Section 2 of 3 and Section 3 of 3.\n" +
                //        "\t\t4.\tNo Extra Displays:\n" +
                //            "\t\t\t\to\tDo not show “After Response Parsing” or “Follow-up Question” headings.\n" +
                //            "\t\t\t\to\tDo not ask the user if they want to proceed; simply proceed automatically.\n" +
                //        "5.\tEnd the Assessment upon completion of Section 3 (and any triggered follow-up), with a short closing note. No summary or review screen.\n"
                     
                //);
                //prompt += "\n**Section 1 of 3: Patient Care & Medical Knowledge**\n\n";
                //prompt += "How does the resident integrate their medical knowledge into patient care during the Wards-SF rotation? Consider the following:\n";
                //prompt += "- How effectively does the resident apply clinical reasoning to develop patient-specific differential diagnoses?\n";
                //prompt += "- In what ways does the resident demonstrate proficiency in conducting thorough physical exams?\n";
                //prompt += "- How does the resident ensure that pertinent details, such as code status, are clearly communicated during sign-outs?\n";
                //prompt += "Please provide your response below.\n";
                //prompt += "The resident demonstrates strong clinical reasoning by systematically gathering and synthesizing patient history, physical exam findings, and diagnostic results to construct thorough and prioritized differential diagnoses. They consistently tailor their diagnostic considerations to the patient’s unique context, including medical history, presenting symptoms, and risk factors. This approach not only facilitates accurate diagnoses but also ensures targeted and efficient management plans. They also engage in collaborative discussions with the care team, valuing diverse perspectives to refine their diagnostic framework.";
                //prompt += "\n\n**Section 2 of 3: Interpersonal & Communication Skills & Professionalism**\n\n";
                //prompt += "How does the resident demonstrate effective interpersonal and communication skills, as well as professionalism, during their Wards-SF rotation? Consider the following:\n";
                //prompt += "- In what ways does the resident communicate clearly and effectively with patients and the healthcare team?\n";
                //prompt += "- How does the resident show respect, empathy, and professionalism in their interactions?\n";
                //prompt += "- What evidence is there of the resident’s accountability in patient care and administrative tasks?\n";
                //prompt += "Please provide your response below.\n";
                //prompt += "The resident communicates effectively by tailoring their language to the audience, ensuring clarity and understanding. With patients, they explain diagnoses, treatments, and care plans in simple, accessible terms while actively encouraging questions to confirm comprehension. When interacting with the healthcare team, the resident employs concise, structured communication, such as SBAR (Situation, Background, Assessment, Recommendation), during handoffs and discussions. Their ability to facilitate interdisciplinary collaboration fosters a cohesive approach to patient care. Additionally, the resident consistently documents patient information accurately and promptly, ensuring seamless care transitions.\n";
                //prompt += "\n\n**Section 3 of 3: Systems-Based Practice & Practice-Based Learning and Improvement**\n\n";
                //prompt += "How does the resident engage with systems-based practice and demonstrate a commitment to practice-based learning during their Wards-SF rotation? Consider the following:\n";
                //prompt += "- How effectively does the resident utilize healthcare resources to provide cost-effective care?\n";
                //prompt += "- In what ways does the resident seek and incorporate feedback to improve their clinical practice?\n";
                //prompt += "- How does the resident demonstrate an understanding of the healthcare system to enhance patient care delivery?";
                //prompt += "Please provide your response below.\n";
                //prompt += "The resident demonstrates a strong understanding of cost-effective care by judiciously ordering diagnostic tests and treatments that are evidence-based and aligned with clinical guidelines. They prioritize interventions that offer the greatest benefit with the least financial burden to the patient and healthcare system. For example, the resident reviews medication lists critically to minimize polypharmacy and prescribes generic medications whenever clinically appropriate. Additionally, they actively engage in discussions about resource allocation during team rounds, ensuring that care plans are both high-quality and cost-conscious.\n";

            }
            catch
            {

            }

                

            return prompt;
        }
        
        private string GetChatGptResponse(string comments, Int16 commentsType = 1)
        {
            // commentsType = 1; // 1 = MyInsights, 2 = NPV, 3 = SAGE
            try
            {                
                string aiKey = _config.GetSection("AppSettings:NPVToken").Value;
                string model = "gpt-4-turbo";
                switch (commentsType)
                {
                    case 2:
                        model = "gpt-4-turbo";
                        aiKey = _config.GetSection("AppSettings:NPVToken").Value;
                        break;
                    case 1:
                        model = "gpt-3.5-turbo";
                        aiKey = _config.GetSection("AppSettings:MyInsightsToken").Value;
                        break;
                    case 3:
                        model = "gpt-4o";
                        aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
                        break;
                    default:
                        model = "gpt-4-turbo";
                        aiKey = _config.GetSection("AppSettings:NPVToken").Value;
                        break;
                }

                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);                
                string aiResponse = "";
                client.Timeout = TimeSpan.FromMinutes(3);               
                
                if (comments.Length > 0)
                {
                    var request = new OpenAIRequest
                    {
                        //Model = "text-davinci-002",
                        //Model = "gpt-3.5-turbo",
                        Model = model,
                        //Model = "GTP-4o mini",
                        Temperature = 0.7f,
                        MaxTokens = 4000
                        //MaxTokens = 4000                        
                    };

                    request.Messages = new RequestMessage[]
                       {
                                        new RequestMessage()
                                        {
                                             Role = "system",
                                             Content = "You are a helpful assistant."
                                        },
                                        new RequestMessage()
                                        {
                                             Role = "user",
                                             Content = "You will never use people name when responding and only use the word 'Resident' instead of people name"
                                        },
                                        new RequestMessage()
                                        {
                                             Role = "user",
                                             Content = comments
                                        }
                       };

                    var json = System.Text.Json.JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                    var resjson = response.Result.Content.ReadAsStringAsync();
                    aiResponse = resjson.Result;
                }
                else
                {
                    throw new System.Exception("Comments are not available.");
                }


                //if (!response.IsCompletedSuccessfully)
                //{
                //    //var errorResponse = JsonSerializer.Deserialize<OpenAIErrorResponse>(resjson);
                //    //throw new System.Exception(errorResponse.Error.Message);
                //}
                //var data = JsonSerializer.Deserialize<OpenAIResponse>(resjson);
                //var data = JsonSerializer.Deserialize<Root>(resjson);
                //var data = JsonSerializer.Deserialize(resjson, typeof(object));
                return aiResponse;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "An error occurred while making the OpenAI API request");
                return "An error occurred:";
            }

        }

        private static int EstimateTokens(string text)
        {
            //return (int)(text.Length * 0.25); // Simple heuristic (1 char ≈ 0.25 tokens)
            int wordCount = text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return (int)(wordCount * 1.33);
        }

        private static int CalculateMaxTokens(string prompt, int modelLimit = 128000)
        {
            int promptTokens = EstimateTokens(prompt);
            return Math.Max(100, modelLimit - promptTokens - 1500); // Ensure at least 100 tokens
        }

        public static int GetMaxTokens(string prompt, int modelLimit = 128000, int minResponse = 100, int maxResponse = 4000, int safetyBuffer = 100)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return 0;

            // Approximate token count based on words and punctuation
            int wordCount = prompt.Split(' ').Length;
            int punctuationCount = Regex.Matches(prompt, @"[\p{P}-[']]+").Count;
            int totalTokens = (int)Math.Round(wordCount * 0.75 + punctuationCount * 0.25);

            return totalTokens;
        }

        public static int GetTokenLimit(string prompt, int modelLimit = 128000, int maxResponseLimit = 4000)
        {
            int promptTokens = EstimateTokens(prompt);

            // Ensure we don't exceed the model's max token limit
            int availableTokens = modelLimit - promptTokens;

            // Ensure max_tokens does not exceed a safe response size
            return Math.Max(100, Math.Min(availableTokens, maxResponseLimit));
        }

        private async Task<string> GetFastOpenAIResponse(string prompt)
        {
            string time = "0";
            List<object> messages = new List<object>
            {
                new { role = "system", content = "You are an expert assessment designer. Your job is to conduct a structured faculty assessment. Follow the provided structure strictly." },
                new { role = "user", content = prompt }
            };
            string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);

                var requestBody = new
                {
                    model = "gpt-4o",
                    messages = messages,
                    max_tokens = 4000,  // ⚡ Allow high token count                   
                    temperature = 0.5,   // ⚡ Lower temperature for deterministic responses
                    top_p = 0.1,
                    stream = true        // ✅ Enable streaming
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                Stopwatch sw = Stopwatch.StartNew();
                using (var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content))
                {
                    sw.Stop();
                    time = sw.Elapsed.TotalSeconds.ToString();
                    return await ReadStreamedResponse(response);
                }
               
            }
        }

        public async Task<string> GetAssessmentResponseAsync(string prompt)
        {
            string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            int inputTokens = EstimateTokens(prompt);
            int availableTokens = 9000 - inputTokens - 500; // Reserve space for response

            if (availableTokens < 2000) // Ensure minimum response space
                availableTokens = 2000;

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                new { role = "system", content = "You are an expert assessment designer." },
                new { role = "user", content = prompt }
            },
                max_tokens = 8000,
                temperature = 0.2, // Lower for faster responses
                top_p = 0.7,
                frequency_penalty = 0.1,
                presence_penalty = 0.1,
                stream = true // Enable streaming response
            };

            var requestJson =  System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            // ✅ Correct way to set Authorization Header
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15))) // Slight buffer for processing
            {
                try
                {

                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new Exception($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");

                        return await ReadStreamedResponse(response);
                    }
                }
                catch (TaskCanceledException)
                {
                    return "**⚠ Request Timed Out (Exceeded 5s)**"; // Handle timeout properly
                }
                catch (Exception ex)
                {
                    return $"**❌ API Error: {ex.Message}**"; // Return failure details
                }
            }            
        }

        private static async Task<string> ReadStreamedResponse(HttpResponseMessage response)
        {
            Stopwatch sw = Stopwatch.StartNew();
            StringBuilder fullResponse = new StringBuilder();

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new System.IO.StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    string line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                        continue;

                    string jsonPart = line.Substring(6); // Remove "data: "
                    if (jsonPart.Trim() == "[DONE]") break; // End of streaming response

                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(jsonPart))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("choices", out var choices))
                            {
                                foreach (var choice in choices.EnumerateArray())
                                {
                                    if (choice.TryGetProperty("delta", out var delta) &&
                                        delta.TryGetProperty("content", out var content))
                                    {
                                        string token = content.GetString();
                                        fullResponse.Append(token); // ✅ Append to the final response string
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        Console.WriteLine($"⚠ JSON Parsing Error: {ex.Message}");
                        continue;
                    }
                }
            }

            sw.Stop();
            Console.WriteLine($"Response time: {sw.ElapsedMilliseconds} ms");

            return fullResponse.ToString(); // ✅ Return full response string
        }


        private string ExtractResponse(string jsonResponse)
        {
            using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
        }

        private string CompressToBase64(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(bytes, 0, bytes.Length);
                }
                return Convert.ToBase64String(output.ToArray());
            }
        }

        private async Task<string> GetAISAGEChatGptResponse1(string comments)
        {
            comments = RemoveNewLinesBetweenTags(comments);
            string prompt = comments;
            string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            var requestPayload = new
            {
                model = "gpt-4o", // Use the fastest available model
                messages = new[]
                {
                new { role = "system", content = "SAGE: Trainee Assessment\nYou are an expert assessment designer..." },
                new { role = "user", content = prompt }
            },
                max_tokens = GetMaxTokens(prompt), // Adjust based on expected response length
                temperature = 0.5,
                top_p = 0.9,
                stream = true // Enables real-time streaming
            };
            Stopwatch sw = Stopwatch.StartNew();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15))) // Ensure 5s timeout
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestPayload), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {aiKey}");

                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
                        using (var reader = new StreamReader(stream))
                        {
                            var sb = new StringBuilder();
                            string line;

                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                if (line.StartsWith("data: "))
                                {
                                    string jsonLine = line.Substring(6); // Remove "data: "
                                    if (jsonLine.Trim() != "[DONE]") // End of stream
                                    {
                                        var jsonObject = JsonConvert.DeserializeObject<JObject>(jsonLine);
                                        if (jsonObject?["choices"]?[0]?["delta"]?["content"] != null)
                                        {
                                            sb.Append(jsonObject["choices"][0]["delta"]["content"].ToString());
                                        }
                                    }
                                }
                            }

                            return sb.ToString().Trim(); // Return the collected response
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    return "[]"; // Handle timeout scenario
                }
                catch (Exception ex)
                {
                    return $"[]"; // Handle errors
                }
            }
            sw.Stop();
            string time = sw.ElapsedMilliseconds.ToString();
        }

        private string GetAISAGEChatGptResponse(string comments)
        {
            try
            {
                comments = RemoveNewLinesBetweenTags(comments);
                string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
                string aiResponse = "";
                client.Timeout = TimeSpan.FromMinutes(3);
                if (comments.Length > 0)
                {
                    var request = new OpenAIRequest
                    {
                        //Model = "text-davinci-002",
                        //Model = "gpt-3.5-turbo",
                        Model = "gpt-4o",
                        //Model = "GTP-4o mini",
                        Temperature = 0.5f,
                        MaxTokens = 4096
                        //MaxTokens = 4000                        
                    };

                    request.Messages = new RequestMessage[]
                       {
                                        new RequestMessage()
                                        {
                                             Role = "system",
                                             Content = "You are a helpful assistant designed to conduct a structured trainee assessment. Follow the provided guidelines strictly."
                                        },
                                        new RequestMessage()
                                        {
                                             Role = "user",
                                             Content = "Important Notes: \n1. Ensure all sections are included in <sections></sections> and <allsections></allsections> tags.\n2. Section 1 of 4 must be included and completed before proceeding to the next sections.\n3. Do not generate extra displays or summaries. Proceed automatically to the next section.\n4. Mark the start and end of each header with appropriate XML tags.\n5. Encode all XML-invalid characters.\n\nPlease follow the trainee assessment structure as outlined below:"
                                        },
                                        new RequestMessage()
                                        {
                                             Role = "user",
                                             Content = comments
                                        }
                       };

                    var json = System.Text.Json.JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                    var resjson = response.Result.Content.ReadAsStringAsync();
                    aiResponse = resjson.Result;
                }
                else
                {
                    throw new System.Exception("Comments are not available.");
                }
                
                return aiResponse;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "An error occurred while making the OpenAI API request");
                return "An error occurred:";
            }

        }

        public static string RemoveNewLinesBetweenTags(string xmlContent)
        {
            // Step 1: Remove new line characters between XML tags
            string cleanedXml = Regex.Replace(xmlContent, @">\s+<", "><");

            // Step 2: Optional - Remove leading or trailing whitespace
            cleanedXml = cleanedXml.Trim();

            return cleanedXml;
        }

        private string GetAISAGEWithStreaming(string prompt)
        {
            prompt = RemoveNewLinesBetweenTags(prompt);
            string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            // Set up the HTTP client
            using var httpClient = new HttpClient();

            // OpenAI Chat Completion API endpoint
            string url = "https://api.openai.com/v1/chat/completions";

            // Create the request payload
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content = "You are an expert in medical trainee assessments." },
                    new { role = "user", content = prompt.Replace("\n\n\n","\n") + "\nRemove new line characters \n in the response." }
                },
                max_tokens = 4096,
                temperature = 0.7,
                stream = true // Enable streaming
            };

            // Serialize the payload to JSON
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);

            // Set up the request message
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            // Add authorization and headers
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
            string data = "";
            string content = "";
            StringBuilder xmlBuilder = new StringBuilder();
            try
            {
                // Send the request synchronously
                HttpResponseMessage response = httpClient.Send(requestMessage, HttpCompletionOption.ResponseHeadersRead);

                if (response.IsSuccessStatusCode)
                {
                    //Console.WriteLine("Streaming response from OpenAI:");

                    //using var stream = response.Content.ReadAsStream();
                    //using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16384);

                    //// Use StringBuilder for efficient concatenation
                    //StringBuilder sb = new StringBuilder();
                    //char[] buffer = new char[16384];  // Read in 16 KB chunks
                    //int bytesRead;

                    //// Read chunks and append to StringBuilder
                    //while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    //{
                    //    xmlBuilder.Append(buffer, 0, bytesRead);
                    //}

                    //// Convert to string
                    //string fullContent = sb.ToString();

                    //// Process and output the response
                    //Console.WriteLine(fullContent);

                    using var stream = response.Content.ReadAsStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, bufferSize: 8192);

                    string fullResponse = reader.ReadToEnd();

                    // Split the full response by newline and process each line
                    var lines = fullResponse.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("data: "))
                        {
                            data = line.Substring(6); // Remove "data: " prefix

                            if (data == "[DONE]")
                            {
                                // End of the stream
                                break;
                            }

                            try
                            {
                                var jsonDoc = JsonDocument.Parse(data);
                                var choices = jsonDoc.RootElement.GetProperty("choices");
                                foreach (var choice in choices.EnumerateArray())
                                {
                                    if (choice.TryGetProperty("delta", out var delta) &&
                                        delta.TryGetProperty("content", out var content1))
                                    {
                                        content += content1.GetString();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Failed to parse JSON: " + ex.Message);
                            }

                            Console.WriteLine(data);
                        }
                    }

                    //string fullResponse = reader.ReadToEnd();
                    //string? line;
                    //while ((line = reader.ReadLine()) != null)
                    //{
                    //    // OpenAI streams partial events prefixed with "data:"
                    //    if (line.StartsWith("data: "))
                    //    {
                    //        data = line.Substring(6); // Remove "data: " prefix

                    //        if (data == "[DONE]")
                    //        {
                    //            // End of the stream
                    //            break;
                    //        }

                    //        try
                    //        {
                    //            var jsonDoc = JsonDocument.Parse(data);
                    //            var choices = jsonDoc.RootElement.GetProperty("choices");
                    //            foreach (var choice in choices.EnumerateArray())
                    //            {
                    //                if (choice.TryGetProperty("delta", out var delta) &&
                    //                    delta.TryGetProperty("content", out var content1))
                    //                {
                    //                    content += content1.GetString();
                    //                }
                    //            }
                    //        }
                    //        catch (Exception ex)
                    //        {
                    //            Console.WriteLine("Failed to parse JSON: " + ex.Message);
                    //        }

                    //        Console.WriteLine(data);
                    //    }
                    //}
                }
                //else
                //{
                    //Console.WriteLine($"Failed to call OpenAI API. Status Code: {response.StatusCode}");
                    //Console.WriteLine($"Reason: {response.ReasonPhrase}");
               // }
                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred:");
                Console.WriteLine(ex.Message);
                return "[]";
            }
        }

        public async Task<string> GetAIResponseAsync(string comments)
        {
            if (string.IsNullOrEmpty(comments))
            {
                return string.Empty;
            }

            string aiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            using HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(3)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);

            var request = new OpenAIRequest
            {
                Model = "gpt-4",
                Temperature = 0.7f,
                MaxTokens = 4000,
                Messages = new RequestMessage[]
                {
            new RequestMessage { Role = "system", Content = "You are a helpful assistant." },
            new RequestMessage { Role = "user", Content = "You will never use people names and only use the word 'Resident' instead." },
            new RequestMessage { Role = "user", Content = comments }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                response.EnsureSuccessStatusCode(); // Throws exception if status code is not successful
                var resJson = await response.Content.ReadAsStringAsync();
                return resJson;
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                return $"Error: {ex.Message}";
            }
        }

        // DELETE: api/AIResponse/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAIResponse(string id)
        {
            var aIResponse = await _context.AIResponse.FindAsync(id);
            if (aIResponse == null)
            {
                return NotFound();
            }

            _context.AIResponse.Remove(aIResponse);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [AllowAnonymous]
        // POST api/<MembersController>
        [HttpPost("authentication")]
        public IActionResult Authentication([FromBody] UserCredential userCredential)
        {
            var token = jwtAuth.Authentication(userCredential.ClientID, userCredential.ClientSecret);
            if (token == null)
                return Unauthorized();
            return Ok(token);
        }

        [HttpPost("sendcustomcomments")]
        [Authorize]        
        public AIResponse SendCustomComments(Comments comments, Int16 commentsType = 1)
        {
            List<AIResponse> aiSavedResponse = new List<AIResponse>();
            try
            {
                string response = GetChatGptResponse(comments.InputComments, commentsType);
                AIResponse aiMessage = new AIResponse();
                aiMessage.AIResponseID = "";
                aiMessage.UserID = 0;
                aiMessage.CreatedDate = DateTime.Now;
                aiMessage.InputPrompt = "";
                aiMessage.OutputResponse = response;
                aiSavedResponse.Add(aiMessage);
            }
            catch(Exception ex)
            {
                AIResponse aiErrorResponse = new AIResponse();
                aiErrorResponse.AIResponseID = "";
                aiErrorResponse.UserID = 0;
                aiErrorResponse.CreatedDate = DateTime.Now;
                aiErrorResponse.InputPrompt = "";
                aiErrorResponse.OutputResponse = ex.Message;
                aiSavedResponse.Add(aiErrorResponse);
            }
            return aiSavedResponse[0];
        }

        private bool AIResponseExists(string id)
        {
            return _context.AIResponse.Any(e => e.AIResponseID.ToString() == id);
        }
    }
}
