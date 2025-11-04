using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.Web.CodeGeneration.Contracts.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
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
using System.Timers;
using System.Xml.Linq;
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
        private readonly HttpClient _httpClient;
        private readonly OpenAIClient _openAIClient;
        private readonly ChatClient chatClient;
        private readonly OpenAIClient _openAIMyInsightsClient;
        private readonly OpenAIClient _openAIAzureClient;
        private readonly ChatClient chatAzureClient;
        public AIResponseController(APIDataBaseContext context,IJwtAuth jwtAuth, IConfiguration config, ILogger<AIResponseController> logger)
        {
            _context = context;
            this.jwtAuth = jwtAuth;
            _config = config;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config["AppSettings:SAGEToken"]);

            _openAIClient = new OpenAIClient(config["AppSettings:SAGEToken"]);
            chatClient = _openAIClient.GetChatClient("gpt-4o-mini");

            //_openAIMyInsightsClient = new OpenAIClient(config["AppSettings:MyInsightsToken"], new OpenAIClientOptions { });
            _openAIMyInsightsClient = new OpenAIClient(
             new ApiKeyCredential(config["AppSettings:MyInsightsToken"]),
             new OpenAIClientOptions
             {
                 NetworkTimeout = TimeSpan.FromSeconds(300)
             }
            );
            //_ = WarmUpAsync();
            //_ = KeepAliveLoopAsync(CancellationToken.None);   // continuous keep-alive

            var azureUri = new Uri(config["AppSettings:AzureUrl"] ?? "https://api.openai.com/");
            _openAIAzureClient = new OpenAIClient(
             new ApiKeyCredential(config["AppSettings:AzureToken"]),
             new OpenAIClientOptions
             {
                 Endpoint = azureUri,
                 NetworkTimeout = TimeSpan.FromSeconds(300)
             }
            );

            chatAzureClient = _openAIAzureClient.GetChatClient("gpt-4o");
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
                    comments = PromptService.GetSagePrompt(input);
                    aiResponse = GetChatGptResponse(comments, 3);
                }                
                //else if (input.InputPrompt.Length > 0) -- Update Existing Comments
                //{
                //    comments = input.InputPrompt;
                //    aiResponse = GetChatGptResponse(comments, 2);
                //}
                else
                {
                    comments = PromptService.GetComments(input);
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

        [HttpPost("GetAPEInsights")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<APEResponse>>> GetAPEInsights([FromBody] AIRequest input)
        {
            List<APEResponse> aiAPEResponse = new List<APEResponse>();
            try
            {
                string prompt = string.Empty;
                string response = string.Empty;
                APEResponse apeResponse = new APEResponse();
                //string requiredCompetencies = "\n\nReturn the results for below Competency/Category's without fail and also include if any other Competency/Category's are available.\n 1) Interpersonal and Communication Skills\n2) Medical Knowledge" +
                //"\n3) Patient Care and Procedural Skills\n4) Practice-Based Learning and Improvement\n5) Professionalism\n6) Systems-Based Practice\nReturn atleast 3 to 4 PITs for one PrimaryACGMECompetencyOrCategory";
                string requiredCompetencies = "\n\nCoverage Rule:\n\r\nThe output must always include all six ACGME Core Competencies:\nInterpersonal and Communication Skills\nMedical Knowledge" +
                    "\nPatient Care and Procedural Skills\nPractice-Based Learning and Improvement\nProfessionalism\nSystems-Based Practice\n" +
                    "If there are additional ACGME categories identified in the AFIs or Comments Source (e.g., Well-Being, Supervision, Faculty Development, etc.), include those as well as separate JSON objects with their PITs.\n" +
                    "This ensures that PEC receives a full framework covering the required six competencies plus any additional program-relevant categories." +
                    "\n\nPIT Count Rule:\n- For each ACGME competency/category, output 3–4 PITs derived from the AFIs.\n- If fewer than 3 PITs exist, include what is available." + 
                    "\n\n\"Important: Frequency is always high. Try to include Frequency as High based on the prompt.\"\n" +
                    "\n\"Important: Include all rotations which are involved on generating the PIT.\"\n";
                
                prompt = await BackEndService.GetAPEAreaOfImprovementsResponse(input, _context, _config);                
                input.AFIPrompt = prompt;
                prompt = prompt + requiredCompetencies;
                response = await GetAPEAIResponse(prompt);
                response = Regex.Replace(response, @"\r\n?|\n", "").Replace("```json", "").Replace("json{", "{").Replace("```", "");
                string unescapedJson = System.Text.RegularExpressions.Regex.Unescape(response);
                var parsed = JToken.Parse(unescapedJson);
                string compactJson = JsonConvert.SerializeObject(parsed, Formatting.None);
                apeResponse.AFIJSON = compactJson;

                prompt = await BackEndService.GetAPEAFIProgramResponse(input, _context, _config);               
                input.AFIProgramPrompt = prompt;
                prompt = prompt + requiredCompetencies;
                response = await GetAPEAIResponse(prompt);
                response = Regex.Replace(response, @"\r\n?|\n", "").Replace("```json", "").Replace("json{", "{").Replace("```", "");
                unescapedJson = System.Text.RegularExpressions.Regex.Unescape(response);
                parsed = JToken.Parse(unescapedJson);
                compactJson = JsonConvert.SerializeObject(parsed, Formatting.None);
                apeResponse.AFIProgramJSON = compactJson;

                if (input.PITPrompt != null && input.PITPrompt.Length > 0)
                {
                    string pitPrompt = input.PITPrompt;
                    //string summary = PromptService.SummarizePITs(apeResponse.AFIJSON);
                    //string summary1 = PromptService.SummarizePITs(apeResponse.AFIProgramJSON);
                    pitPrompt = pitPrompt.Replace("[Input]", "\n**AFI Summary JSON**\n" + apeResponse.AFIJSON + "\n\n**AFI Program Summary JSON**\n" + apeResponse.AFIProgramJSON);
                    input.PITPrompt = pitPrompt;
                    pitPrompt = pitPrompt + requiredCompetencies;
                    string pitResponse = await GetAPEAIResponse(pitPrompt);
                    pitResponse = Regex.Replace(pitResponse, @"\r\n?|\n", "").Replace("```json", "").Replace("json{", "{").Replace("```", "");
                    unescapedJson = System.Text.RegularExpressions.Regex.Unescape(pitResponse);
                    parsed = JToken.Parse(unescapedJson);
                    compactJson = JsonConvert.SerializeObject(parsed, Formatting.None);
                    apeResponse.PITJSON = compactJson;
                }
                else
                {
                    apeResponse.PITJSON = "{}";
                }

                aiAPEResponse.Add(apeResponse);
            }
            catch(Exception ex)
            {
                APEResponse apeResponse = new APEResponse();
                apeResponse.PITJSON = $"{{\"error\":\"{ex.Message}\"}}";
                apeResponse.AFIJSON = "";
                aiAPEResponse.Add(apeResponse);
            }
            await BackEndService.InsertAPEResponses(input, aiAPEResponse[0], _context);
            return aiAPEResponse;
        }             
                     
        

        [HttpPost("TestModel5")]
        [AllowAnonymous]
        public async Task<string> TestGPT5Model()
        {
            return await GetGPT5Response("");
        }

        [HttpPost("TestAzureDomain")]
        [AllowAnonymous]
        public async Task<string> TestAzureDomain()
        {
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);

            string systemMessages = "You are good at poetry.\n Return result in 2 to 3 seconds.";

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(systemMessages),
                ChatMessage.CreateUserMessage("Plese provide one good poetry.")
            };

            StringBuilder sb = new StringBuilder();           
            var options = new ChatCompletionOptions
            {
                Temperature = 1,
                TopP = 1,
                PresencePenalty = 0,
                FrequencyPenalty = 0,
                MaxOutputTokenCount = 400
            };

            try
            {
                // ✅ Streaming response from OpenAI
                await foreach (var update in chatAzureClient.CompleteChatStreamingAsync(messages, options))
                {
                    if (update.ContentUpdate.Count > 0)
                    {
                        string token = update.ContentUpdate[0].Text;
                        sb.Append(token);

                    }
                }
            }
            catch (Exception ex)
            {

            }

            sw.Stop();
            time = sw.Elapsed.TotalSeconds.ToString();
            // let prefetch complete in background
            // _ = prefetchTask;
            return sb.ToString(); 
        }

        [HttpPost("MyInsightsRotations")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<MyInsightsResponse>>> MyInsightsSummary([FromBody] MyInsightsRotationSummary input)
        {
            List<MyInsightsResponse> aiResponse = new List<MyInsightsResponse>();
            try
            {                
                string prompt = await BackEndService.GetRotationMyInsightsNarrativeResponse(input, _context, _config);
                string systemMessage = "You are an automated educational feedback formatter. " +
                    "You must return output strictly in the JSON structure provided below — with the same keys, order, and nesting. " +
                    "Do not include any rotation names or program identifiers inside Actionable Feedback values. " +
                    "Each feedback item must be a plain, rotation-neutral, and actionable statement.\n\n### NON-NEGOTIABLE RULES\n" +
                    "1. Output **only JSON**, never markdown, text, or explanation.\n" +
                    "2. The JSON structure, keys, and order must match exactly the template below.\n" +
                    "3. All keys are required — none may be omitted or renamed.\n" +
                    "4. The output must be syntactically valid JSON (parsable by standard JSON parsers).\n" +
                    "5. Actionable Feedback arrays must contain multiple feedback statements, **without any rotation name prefix or label** (e.g., “Consult Service:” or “Urology:” are strictly forbidden inside Actionable Feedback text).\n" +
                    "6. Replace placeholder tokens ({{...}}) with real, contextually generated text based on provided evaluation data.\n" +
                    "7. If data is missing, set the Summary field exactly to: \"Insufficient Data to Assess.\".\n" +
                    "8. Keep a professional, neutral tone; never include identifying details, names, or departments.\n" +
                    "9. Do not add any additional keys, sections, or metadata.\n10. Never add rotation names, program identifiers, or prefixes inside feedback text.\n" +
                    "11. Dates must appear in MM/DD/YYYY format when applicable.\n12. Output only the JSON object — no wrapping quotes, markdown, or code fences.\n\n### EXACT JSON STRUCTURE TO RETURN " +
                    "{\r\n  \"RotationGoalsandLearningOutcomes\": {\r\n    \"Initial3Months\": {\r\n      \"DateRange\": \"{{StartDate}} to {{MidDate}}\",\r\n      \"Summary\": \"{{DynamicSummaryInitial}}\"\r\n    },\r\n    \"MostRecent3Months\": {\r\n      \"DateRange\": \"{{MidDate}} to {{EndDate}}\",\r\n      \"Summary\": \"{{DynamicSummaryRecent}}\"\r\n    },\r\n    \"ActionableFeedback\": [\r\n      \"{{ActionablePoint1}}\",\r\n      \"{{ActionablePoint2}}\",\r\n      \"{{ActionablePoint3}}\"\r\n    ]\r\n  },\r\n\r\n  \"TeachingandSupervisionQuality\": {\r\n    \"Initial3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryInitial}}\"\r\n    },\r\n    \"MostRecent3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryRecent}}\"\r\n    },\r\n    \"ActionableFeedback\": [\r\n      \"{{ActionablePoint1}}\",\r\n      \"{{ActionablePoint2}}\",\r\n      \"{{ActionablePoint3}}\"\r\n    ]\r\n  },\r\n\r\n  \"InterprofessionalCollaborationandSystemsBasedPractice\": {\r\n    \"Initial3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryInitial}}\"\r\n    },\r\n    \"MostRecent3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryRecent}}\"\r\n    },\r\n    \"ActionableFeedback\": [\r\n      \"{{ActionablePoint1}}\",\r\n      \"{{ActionablePoint2}}\",\r\n      \"{{ActionablePoint3}}\"\r\n    ]\r\n  },\r\n\r\n  \"ClinicalWorkloadandAutonomy\": {\r\n    \"Initial3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryInitial}}\"\r\n    },\r\n    \"MostRecent3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryRecent}}\"\r\n    },\r\n    \"ActionableFeedback\": [\r\n      \"{{ActionablePoint1}}\",\r\n      \"{{ActionablePoint2}}\",\r\n      \"{{ActionablePoint3}}\"\r\n    ]\r\n  },\r\n\r\n  \"WellnessandSupport\": {\r\n    \"Initial3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryInitial}}\"\r\n    },\r\n    \"MostRecent3Months\": {\r\n      \"Summary\": \"{{DynamicSummaryRecent}}\"\r\n    },\r\n    \"ActionableFeedback\": [\r\n      \"{{ActionablePoint1}}\",\r\n      \"{{ActionablePoint2}}\",\r\n      \"{{ActionablePoint3}}\"\r\n    ]\r\n  },\r\n\r\n  \"OverallMyInsights\": {\r\n    \"Strengths\": [\r\n      \"{{DynamicStrength1}}\",\r\n      \"{{DynamicStrength2}}\"\r\n    ],\r\n    \"AreasforImprovement\": [\r\n      \"{{DynamicImprovement1}}\",\r\n      \"{{DynamicImprovement2}}\"\r\n    ],\r\n    \"ActionableSteps\": [\r\n      \"{{DynamicAction1}}\",\r\n      \"{{DynamicAction2}}\"\r\n    ],\r\n    \"ShortTermGoals\": [\r\n      \"{{ShortTermGoal1}}\"\r\n    ],\r\n    \"LongTermGoals\": [\r\n      \"{{LongTermGoal1}}\"\r\n    ]\r\n  }\r\n}\r\n";
                string insightResponse = await MyInsightsGPT5Response(systemMessage, prompt + "\n Important: Please return Expected JSON Output Format");
                MyInsightsResponse summaryResponse = new MyInsightsResponse();
                summaryResponse.SummaryJSON = insightResponse;
                input.SummaryJSON = insightResponse;
                aiResponse.Add(summaryResponse);
                DataSet dsData = BackEndService.SaveMyInsightsFromJson(_context, input);

            }
            catch (System.Exception ex)
            {
                MyInsightsResponse summaryResponse = new MyInsightsResponse();
                summaryResponse.SummaryJSON = "[]";                
                aiResponse.Add(summaryResponse);
                _logger.LogError(ex, "An error occurred while making the OpenAI API request");
               
            }
            return aiResponse;
        }     
                  
       

        [HttpPost("MyInsightsSummary")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<MyInsightsResponse>>> MyInsightsSummary([FromBody] AIRequest input)
        {
            List<MyInsightsResponse> aiResponse = new List<MyInsightsResponse>();
            DataSet dsComments = BackEndService.GetMyInsightsSummaryComments(_context, input);
            string myInsightPrompt = PromptService.GetMyInsightsSummaryPrompt();
            //string myInsightComments = await PromptService.GetMyInsightsComments(dsComments, _openAIMyInsightsClient);
            string myInsightComments = await InsightsSummarizer.GetMyInsightsComments(dsComments, _openAIMyInsightsClient);
            string systemMessage = "You are ChatGPT, a helpful, structured, and expert assistant.\r\nYou produce polished, thematic, well-organized summaries.\r\nWrite as if your output will be directly pasted into a professional report.\r\nUse clear section headers, confident academic language, and smooth narrative structure." +
                "\r\nNever ask follow-up questions. Output should be final.\nFor every competency, you MUST include a property named `ProgressionByPGY`\n`ProgressionByPGY` MUST be a non-empty array.";
            string insightResponse = await MyInsightsGPT5Response(systemMessage, myInsightPrompt + "\n" + myInsightComments);
            MyInsightsResponse summaryResponse = new MyInsightsResponse();
            summaryResponse.SummaryJSON = insightResponse;
            aiResponse.Add(summaryResponse);
            DataSet dsResult = BackEndService.InsertDepartmentalSummaryFromJson(_context, input, summaryResponse);
            return aiResponse;
        }

        [HttpPost("SubmitSAGE")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SAGEResponse>>> GetSAGEResponse([FromBody] AIRequest input)
        {
            string comments = "";
            string aiResponse = "";
            string minifiedJson = "[]";
            Stopwatch totalTime = Stopwatch.StartNew();
            double totalSeconds = 0, promptDBSeconds = 0, historySeconds = 0;
            int apiAttempts = 0;
            TimeHistory timeHistory = new TimeHistory();
            List<SAGEResponse> aiSavedResponse = new List<SAGEResponse>();
            try
            {
                if (input.EvaluationID > 0)
                {
                    Stopwatch promptDBTime = Stopwatch.StartNew();
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
                        promptDBTime.Stop();
                        promptDBSeconds = promptDBTime.Elapsed.TotalSeconds;
                        timeHistory.PromptDBSeconds = promptDBSeconds;
                        DataTable dtPrompt = dsSageData.Tables[0];
                        DataTable dtQuestions = dsSageData.Tables[1];
                        DataTable dtResponses = dsSageData.Tables[2];
                        //DataTable dtDefaultJSON = dsSageData.Tables[3];
                        Int64 settingsID = 0;
                        string apiFileContent = "";
                        if (dtPrompt.Rows.Count > 0)
                        {
                            defaultJSON = dtPrompt.Rows[0]["DefaultJSON"].ToString();
                            input.SageRequest = dtPrompt.Rows[0]["AIJSON"].ToString();
                            settingsID = Convert.ToInt64(dtPrompt.Rows[0]["SettingsID"].ToString());
                            apiFileContent = dtPrompt.Rows[0]["APIFileContent"].ToString();
                        }
                        if (dtResponses.Rows.Count > 0 && input.SageRequest.Length > 2)
                        {
                            comments = dtResponses.Rows[0]["AIPrompt"].ToString();
                            if (dtPrompt.Rows.Count > 0)
                            {
                                comments = comments.Replace("[Program Type]", dtPrompt.Rows[0]["DepartmentName"].ToString());
                                comments = comments.Replace("[Rotation]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[ Rotation]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[ Rotation ]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[Rotation Name]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[Setting]", dtPrompt.Rows[0]["ActivityName"].ToString());
                                comments = comments.Replace("[Level]", dtPrompt.Rows[0]["PGYLevel"].ToString());
                                comments = comments.Replace("[User Type]", dtPrompt.Rows[0]["UserTypeName"].ToString());
                                comments = comments.Replace("[Specialty]", dtPrompt.Rows[0]["SpecialityName"].ToString());
                            }
                        }
                        else
                        {
                            string templateIDs = "";
                            string subjectUserID = "0";
                            if (dtPrompt.Rows.Count > 0)
                            {
                                comments = dtPrompt.Rows[0]["FileContent"].ToString();
                                //comments = await CompressClientPrompt(comments, settingsID);
                                //return null;
                                if (string.IsNullOrEmpty(apiFileContent))
                                {
                                    comments = dtPrompt.Rows[0]["FileContent"].ToString();
                                    //comments = await CompressClientPrompt(comments, settingsID);
                                }
                                else
                                {
                                    comments = apiFileContent;
                                }

                               // return null;
                                templateIDs = dtPrompt.Rows[0]["TemplateIDs"].ToString();
                                subjectUserID = dtPrompt.Rows[0]["SubjectUserID"].ToString();
                                input.RotationName = dtPrompt.Rows[0]["RotationName"].ToString();
                                input.DepartmentName = dtPrompt.Rows[0]["DepartmentName"].ToString();
                                input.TrainingLevel = dtPrompt.Rows[0]["PGYLevel"].ToString();
                                input.ActivityName = dtPrompt.Rows[0]["ActivityName"].ToString();
                                if (comments.Length == 0)
                                {
                                    comments = PromptService.GetSagePrompt(input);
                                }
                                comments = comments.Replace("</br>", "\n");
                                comments = comments.Replace("<br>", "\n");
                                comments = comments.Replace("[Program Type]", dtPrompt.Rows[0]["DepartmentName"].ToString());
                                comments = comments.Replace("[Rotation]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[ Rotation]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[ Rotation ]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[Rotation Name]", dtPrompt.Rows[0]["RotationName"].ToString());
                                comments = comments.Replace("[Setting]", dtPrompt.Rows[0]["ActivityName"].ToString());
                                comments = comments.Replace("[Level]", dtPrompt.Rows[0]["PGYLevel"].ToString());
                                comments = comments.Replace("[User Type]", dtPrompt.Rows[0]["UserTypeName"].ToString());
                                comments = comments.Replace("[Specialty]", dtPrompt.Rows[0]["SpecialityName"].ToString());

                            }
                            else
                            {
                                comments = PromptService.GetSagePrompt(input);
                            }
                            // Get Last 12 months historical data.
                            Stopwatch promptDBHistory = Stopwatch.StartNew();
                            string history = BackEndService.GetPreviousHistory(input, templateIDs, Convert.ToInt64(subjectUserID), _context);
                            promptDBHistory.Stop();
                            historySeconds = promptDBHistory.Elapsed.TotalSeconds;
                            timeHistory.HistorySeconds = historySeconds;
                            // Summarize the comments
                            if (history.Length > 0)
                            {
                                //history = await SummarizeHistoricalData(history, 2000);
                            }
                            comments = comments.Replace("[Historical Data]", history);
                        }
                    }
                    comments = RemoveHTMLTags(comments);
                    string sageQuestions = "";
                    Int32 lastSection = 1;
                    Int32 totalSections = 1;
                    if (input.SageRequest != null && input.SageRequest.Length > 2)
                    {
                        sageQuestions = SageExtraction.ConvertLastJsonToFormattedText(input.SageRequest, ref lastSection, ref totalSections);
                        if (sageQuestions.Length > 0)
                        {

                            sageQuestions = sageQuestions.Replace("</br>", "\n");
                            sageQuestions = sageQuestions.Replace("<br>", "\n");
                        }
                    }
                    else
                    {
                        sageQuestions = "IMPORTANT: For this response, only generate section 1.\n";
                    }
                    //string aiComments = GetAISAGEWithStreaming(comments + "\n" + sageQuestions + "\n include <section> tag between the tag <sections></sections>");
                    //string aiComments = await GetAISAGEChatGptResponse1(comments + "\n" + sageQuestions + "\n include <section> tag between the tag <sections></sections>");
                    apiAttempts++;
                    Stopwatch aiResponseWatch = Stopwatch.StartNew();
                    string aiComments = await GetFastOpenAIResponse3(sageQuestions + "\n" + comments + "\n include <mainsection></mainsection> without fail. \n Answer is always empty in the response for example <answer></answer>" , lastSection, totalSections);
                    aiResponseWatch.Stop();
                    timeHistory.AIResponseSeconds = aiResponseWatch.Elapsed.TotalSeconds;
                    string extractJSON = SageExtractData(aiComments);
                    JToken parsedJson = JToken.Parse(extractJSON);
                    minifiedJson = JsonConvert.SerializeObject(parsedJson, Formatting.None);
                    if (input.SageRequest.Length > 0 && minifiedJson.Length > 0)
                    {
                        minifiedJson = SageExtraction.MergeJson(input.SageRequest, minifiedJson);
                        extractJSON = minifiedJson;
                    }
                    minifiedJson = UpdateRequestJSON(minifiedJson, input.SageRequest);

                    Int32 sectionCount = GetSectionsCount(extractJSON);
                    Int32 allSectionsCount = GetAllSectionsCount(extractJSON);
                    string allSectionsPrompt = "";
                    if (allSectionsCount == 0)
                    {
                        allSectionsPrompt = "\nSections are missed in the tag <allsections></allsections>, Please include.";
                    }
                    if (sectionCount == 0)
                    {
                        aiComments = await GetFastOpenAIResponse2(comments + "\n" + sageQuestions + "\nSections are missed in the tag <sections></sections>, Please include." + allSectionsPrompt + "\n include <section> tag between the tag <sections></sections>");
                        apiAttempts++;
                        extractJSON = SageExtractData(aiComments);
                        sectionCount = GetSectionsCount(extractJSON);
                        if (sectionCount == 0)
                        {
                            extractJSON = minifiedJson;
                        }
                        else if (input.SageRequest.Length > 0 && minifiedJson.Length > 0)
                        {
                            extractJSON = SageExtraction.MergeJson(input.SageRequest, minifiedJson);
                        }
                    }
                    else if (lastSection > sectionCount && lastSection <= totalSections)
                    {
                        string updatedPrompt = $"{comments} \n{sageQuestions} \n Section {lastSection} of {totalSections} is missed, please include. {allSectionsPrompt}\n include <section> tag between the tag <sections></sections>";
                        aiComments = await GetFastOpenAIResponse1(updatedPrompt);
                        apiAttempts++;
                        extractJSON = SageExtractData(aiComments);
                        sectionCount = GetSectionsCount(extractJSON);
                        if (sectionCount == 0)
                        {
                            extractJSON = minifiedJson;
                        }
                        else if (input.SageRequest.Length > 0 && minifiedJson.Length > 0)
                        {
                            extractJSON = SageExtraction.MergeJson(input.SageRequest, minifiedJson);
                        }
                    }

                    sectionCount = GetSectionsCount(extractJSON);
                    if (lastSection > sectionCount && lastSection <= totalSections && defaultJSON.Length > 0)
                    {
                        // Include sections manually if API returns invalid data
                        extractJSON = InsertSection(extractJSON, defaultJSON, (lastSection - 1));
                    }

                    parsedJson = JToken.Parse(extractJSON);
                    minifiedJson = JsonConvert.SerializeObject(parsedJson, Formatting.None);
                    minifiedJson = UpdateRequestJSON(minifiedJson, input.SageRequest);
                    SAGEResponse sageResponse = new SAGEResponse();
                    sageResponse.EvaluationID = input.EvaluationID;
                    sageResponse.ResponseJSON = Regex.Replace(minifiedJson, @"\r\n?|\n", "");
                    aiSavedResponse.Add(sageResponse);
                    minifiedJson = ChangeJSONOrder(minifiedJson);
                    DataSet dsData = ConvertJsonToDataSet(minifiedJson);
                    totalTime.Stop();
                    totalSeconds = totalTime.Elapsed.TotalSeconds;
                    timeHistory.TotalSeconds = totalSeconds;
                    timeHistory.ApiAttempts = apiAttempts;
                    DataSet dsResultSet = BackEndService.SaveSageResponse(_context, dsData, input, aiResponse, comments, minifiedJson, timeHistory);
                    if (dsResultSet != null && dsResultSet.Tables.Count > 0)
                    {
                        DataTable dtEvaluationQuestions = dsResultSet.Tables[0];
                        sageResponse.ResponseJSON = UpdateJSONQuestionIDs(dtEvaluationQuestions, minifiedJson);
                        BackEndService.SaveSageResponse(_context,sageResponse.ResponseJSON, input);
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

        
        private Int32 GetSectionsCount(string extractJSON)
        {
            return SageExtraction.GetSectionsCount(extractJSON);
        }

        private string SageExtractData(string aiComments)
        {
            return SageExtraction.ExtractData(aiComments);
        }
        //SageExtraction.UpdateJSONQuestionIDs(dtEvaluationQuestions, minifiedJson)
        private string UpdateJSONQuestionIDs(DataTable dtEvaluationQuestions, string minifiedJson)
        {
            return SageExtraction.UpdateJSONQuestionIDs(dtEvaluationQuestions, minifiedJson);
        }

        private DataSet ConvertJsonToDataSet(string json)
        {
            return SageExtraction.ConvertJsonToDataSet(json);
        }

        // SageExtraction.InsertSection(extractJSON, defaultJSON, (lastSection - 1))

        private string InsertSection(string extractJSON, string defaultJSON, Int32 position)
        {
            return SageExtraction.InsertSection(extractJSON, defaultJSON, position);
        }

        // SageExtraction.UpdateRequestJSON(minifiedJson, input.SageRequest);
        private string UpdateRequestJSON(string minifiedJson, string requestJson)
        {
            return SageExtraction.UpdateRequestJSON(minifiedJson, requestJson);
        }
        // SageExtraction.ChangeJSONOrder(minifiedJson)
        private string ChangeJSONOrder(string minifiedJson)
        {
            return SageExtraction.ChangeJSONOrder(minifiedJson);
        }

        //SageExtraction.ConvertJsonToFormattedText(input.SageRequest, ref lastSection, ref totalSections)
        private string ConvertJsonToFormattedText(string sageRequest, ref Int32 lastSection, ref Int32 totalSections)
        {
            return SageExtraction.ConvertJsonToFormattedText(sageRequest, ref lastSection, ref totalSections);
        }
        //SageExtraction.GetAllSectionsCount(extractJSON)
        private Int32 GetAllSectionsCount(string extractJSON)
        {
            return SageExtraction.GetAllSectionsCount(extractJSON);
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

        private string RemoveHTMLTags(string comments)
        {
            // Replace <li> and </li> with newlines
            string text = Regex.Replace(comments, @"\s*<li>\s*", "")
                               .Replace("</li>", "\n");
            text = text.Replace("<ul>", "\n").Replace("</ul>", "\n");
            text = text.Replace("<ol>", "\n").Replace("</ol>", "\n");
            // Remove remaining HTML tags like <ul> </ul>
            //text = Regex.Replace(text, "<.*?>", "").Trim();
            return text;
        }           
                       

        private MatchCollection ExtractData(string airesponse)
        {
            string[] words = { "" };
            string pattern = string.Format(@"<{0}>(.*?)<\/{0}>", "guide");
            Regex regex = new Regex(pattern, RegexOptions.Singleline);

            // Extract Matches
            return regex.Matches(airesponse);
        }              

        private async Task<string> SummarizeHistoricalData(string text, int maxTokens = 4000)
        {
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            string aiKey = _config.GetSection("AppSettings:MyInsightsToken").Value;
            string systemMessage = "You are an expert in summarizing user comments.";
            string userMessage = $"Summarize the following text line by line, keep it concise:\n\n{text}";

            List<object> messages = new List<object>
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = userMessage }
            };

            using (var client = new HttpClient())
            {                
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = messages,
                    max_tokens = 500,
                    temperature = 0,
                    top_p = 1,
                    stream = false
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var result = await response.Content.ReadAsStringAsync();

                dynamic json = JsonConvert.DeserializeObject(result);
                sw.Stop();
                time = sw.Elapsed.TotalSeconds.ToString();
                return json?.choices?[0]?.message?.content ?? "";
            }

        }
        

        private async Task<string> GetAPEAIResponse(string prompt, Int16 promptType = 1)
        {
            string time = "0";            
            StringBuilder delta = new StringBuilder();           
            List<object> messages = new List<object>
            {
                new { role = "system", content = "You are a JSON generator. Always output ONLY valid JSON. The JSON must include ALL PITs present in the user’s input. Do not stop early. Do not skip any PIT. Do not summarize.\n\n" },
                new { role = "user", content = prompt }
            };
            string aiKey = _config.GetSection("AppSettings:MyInsightsAPEToken").Value;           

            using (var client = new HttpClient())
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
                var requestBody = new
                {
                    model = "gpt-4o",
                    messages = messages,
                    max_tokens = 9000,
                    temperature = 0,
                    top_p = 0.1,
                    stream = false
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var result = await response.Content.ReadAsStringAsync();

                dynamic json = JsonConvert.DeserializeObject(result);
                return json?.choices?[0]?.message?.content ?? "";
            }

            //using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions"))
            //{
            //    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);

            //    var requestBody = new
            //    {
            //        model = "gpt-4o",
            //        messages = messages,
            //        max_tokens = 8000,
            //        temperature = 0,
            //        top_p = 0.1,
            //        stream = true
            //    };

            //    request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            //    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            //    using (var stream = await response.Content.ReadAsStreamAsync())
            //    using (var reader = new StreamReader(stream))
            //    {
            //        while (!reader.EndOfStream)
            //        {
            //            var line = await reader.ReadLineAsync();

            //            if (string.IsNullOrWhiteSpace(line))
            //                continue;

            //            if (line.StartsWith("data: "))
            //            {
            //                var json = line.Substring("data: ".Length);

            //                if (json == "[DONE]") break;

            //                var evt = JsonConvert.DeserializeObject<dynamic>(json);

            //                string responseChunk = evt?.choices[0]?.delta?.content;
            //                if (!string.IsNullOrEmpty(responseChunk))
            //                {
            //                    //Console.Write(delta); // 🔥 append delta to your buffer
            //                    delta.Append(responseChunk);
            //                }
            //            }
            //        }
            //    }
            //}

            //using (var client = new HttpClient())
            //{
            //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
            //    client.Timeout = Timeout.InfiniteTimeSpan; // 🔥 Disable timeout for streaming
            //    var requestBody = new
            //    {
            //        model = "gpt-4o",
            //        messages = messages,
            //        max_tokens = 8000,  // ⚡ Allow high token count                   
            //        temperature = 0,   // ⚡ Lower temperature for deterministic responses
            //        top_p = 0.1,
            //        stream = true        // ✅ Enable streaming
            //    };

            //    var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            //    Stopwatch sw = Stopwatch.StartNew();
            //    using (var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content))
            //    {
            //        sw.Stop();
            //        time = sw.Elapsed.TotalSeconds.ToString();
            //        return await ReadStreamedResponse1(response);
            //    }

            //}
            return delta.ToString();
        }

        private async Task<string> SummarizeCommentsWithGPT(string comments)
        {
            string apiKey = _config.GetSection("AppSettings:MyInsightsToken").Value;
            string model = "gpt-4o";
            var messages = new List<object>
            {
                new { role = "system", content = "You summarize database field descriptions." },
                new { role = "user", content = "Summarize the following comments into short bullet points:\n" + comments }
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var requestBody = new
                {
                    model = model,
                    messages = messages,
                    max_tokens = 4000,
                    temperature = 0.3
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var result = await response.Content.ReadAsStringAsync();

                dynamic json = JsonConvert.DeserializeObject(result);
                return json?.choices?[0]?.message?.content ?? "";
            }
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

        private static int GetMaxTokens(string prompt, int modelLimit = 128000, int minResponse = 100, int maxResponse = 4000, int safetyBuffer = 100)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return 0;

            // Approximate token count based on words and punctuation
            int wordCount = prompt.Split(' ').Length;
            int punctuationCount = Regex.Matches(prompt, @"[\p{P}-[']]+").Count;
            int totalTokens = (int)Math.Round(wordCount * 0.75 + punctuationCount * 0.25);

            return totalTokens;
        }

        private static int GetTokenLimit(string prompt, int modelLimit = 128000, int maxResponseLimit = 4000)
        {
            int promptTokens = EstimateTokens(prompt);

            // Ensure we don't exceed the model's max token limit
            int availableTokens = modelLimit - promptTokens;

            // Ensure max_tokens does not exceed a safe response size
            return Math.Max(100, Math.Min(availableTokens, maxResponseLimit));
        }

        private string CompressToBase64(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            byte[] rawBytes = Encoding.UTF8.GetBytes(input);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                {
                    gzip.Write(rawBytes, 0, rawBytes.Length);
                }
                return Convert.ToBase64String(output.ToArray());
            }
        }

        private string DecompressFromBase64(string base64Input)
        {
            if (string.IsNullOrEmpty(base64Input)) return base64Input;

            byte[] compressedBytes = Convert.FromBase64String(base64Input);
            using (var input = new MemoryStream(compressedBytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private string OptimizePrompt(string prompt)
        {
            // 1. Normalize multiple spaces into one
            string cleaned = Regex.Replace(prompt, @"[ \t]+", " ");

            // 2. Collapse multiple newlines into a single newline
            cleaned = Regex.Replace(cleaned, @"\n{2,}", "\n");

            // 3. Remove unnecessary spaces between XML/HTML tags
            cleaned = Regex.Replace(cleaned, @">\s+<", "><");

            // 4. Trim leading/trailing whitespace
            cleaned = cleaned.Trim();

            return cleaned;
        }

        private string UpdateXMLTags(string prompt, bool isCompress)
        {
            var tagMap = new Dictionary<string, string>
            {
                { "sections", "ss" },
                { "section", "sc" },
                { "sectionnum", "sn" },
                { "sectionfullname", "sfn" },
                { "name", "nm" },
                { "fullname", "fn" },
                { "mainquestion", "mq" },
                { "guidequestions", "gq" },
                { "guidequestion", "gp" },
                { "followupquestions", "fq" },
                { "followupquestion", "fp" },
                { "endmessage", "em" },
                { "wait", "w" },
                {"allsections","ac" },
                { "totalsections", "ts"}
            };
            if (isCompress)
            {
                foreach (var kvp in tagMap)
                {
                    // Replace opening tags
                    prompt = prompt.Replace($"<{kvp.Key}>", $"<{kvp.Value}>");
                    // Replace closing tags
                    prompt = prompt.Replace($"</{kvp.Key}>", $"</{kvp.Value}>");
                }
            }
            else
            {
                foreach (var kvp in tagMap)
                {
                    prompt = prompt.Replace($"<{kvp.Value}>", $"<{kvp.Key}>")
                             .Replace($"</{kvp.Value}>", $"</{kvp.Key}>");
                }
            }
            return prompt;
        }

        private (string systemMessage, string userMessage) SplitPrompt(string fullPrompt)
        {
            string[] parts = Regex.Split(fullPrompt, @"(?i)input:");
            string part1 = parts.Length > 0 ? parts[0].Trim() : "";
            string part2 = parts.Length > 1 ? parts[1].Trim() : "";

            string canonicalRules = @"
            You are an assessment generator.
            Rules:
            - Output only valid XML, never text or commentary.
            - Use compact schema:
            <section>
              <sectionnum>n</sectionnum>
              <name>short name</name>
              <fullname>Section n of 10: full name</fullname>
              <mainquestion>main question text</mainquestion>
              <guidequestions>
                <guidequestion>Prompt 1</guidequestion>
                <guidequestion>Prompt 2</guidequestion>
                <guidequestion>Prompt 3</guidequestion>
              </guidequestions>
              <followupquestions>
                <followupquestion>Optional followup 1</followupquestion>
                <followupquestion>Optional followup 2</followupquestion>
              </followupquestions>
              <endmessage>** Your anonymous assessment has been submitted. Thank you. **</endmessage>
            </section>
            - Do not include evaluator answers or examples.
            - Do not generate empty tags (omit optional blocks when not needed).
            - Keep mainquestion ≤30 words, guidequestion ≤20 words.           
            ";

                        string systemMessage = UpdateXMLTags(canonicalRules + "\n\n" + part1, true);                        

                        string userMessage = UpdateXMLTags($@"Input:\n{part2}",true);

                        return (systemMessage.Trim(), userMessage.Trim());
        }

        private async Task<string> CompressClientPrompt(string prompt, Int64 id)
        {
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);

            string systemMsg =
     "You are a prompt optimizer. Compress the narrative instructions in the client prompt to ~600 tokens. " +
     "Rules:\n" +
     "- Do NOT remove or change placeholders ([Program Type], [Rotation], [Training Level], [Previous History], [User Type], [Setting], [Level], [Historical Data]).\n" +
     "- Do NOT change the XML schema or example format. Copy all XML tags and structure exactly as provided.\n" +
     "- Only shorten verbose natural language. Keep rules intact.\n" +
     "- Return output as PLAIN TEXT, with newline characters (\\n) instead of HTML tags.\n" +
     "- Do not include <br>, <p>, or any HTML formatting.\n" +
     "- Always return the FULL compressed prompt, never truncated.\n" +
     "- End with <endprompt>.";

            string userMsg =
                "Compress the following client prompt while keeping the XML schema unchanged:\n\n" + prompt;
            var requestBody = new
            {
                model = "gpt-4o",   // or "gpt-4o-mini" for faster responses
                messages = new object[]
            {
                new { role = "system", content = systemMsg },
                new { role = "user", content = userMsg }
            },
                max_tokens = EstimateTokens(prompt),
                temperature = 0,
                top_p = 1
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();

            var parsed = JObject.Parse(result);
            string aiMessage = parsed["choices"]?[0]?["message"]?["content"]?.ToString();
            aiMessage = aiMessage.Replace("\\n", "\n");
            BackEndService.UpdateAISageSettingsPrompt(_context, id, aiMessage);
            return aiMessage;

        }

        private async Task WarmUpAsync()
        {
            var messages = new[]
                        {
                ChatMessage.CreateSystemMessage("Warmup ping")
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1
            };

            var response = await chatClient.CompleteChatAsync(messages, options);
        }

        private async Task KeepAliveLoopAsync(CancellationToken token)
        {
            var msgs = new[] { ChatMessage.CreateSystemMessage("ping") };
            var opt = new ChatCompletionOptions { MaxOutputTokenCount = 1 };

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // very small request just to keep the model warm
                    await chatClient.CompleteChatAsync(msgs, opt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"KeepAlive failed: {ex.Message}");
                }

                // wait 45 s (adjust to 30 s if you still see cold spikes)
                await Task.Delay(TimeSpan.FromSeconds(45), token);
            }
        }


        private async Task PrefetchSectionAsync(int sectionNumber)
        {
            var chatClient = _openAIClient.GetChatClient("gpt-4o");
            var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage("Prefetch next section context only."),
            ChatMessage.CreateUserMessage($"Prepare for section {sectionNumber}. Do not output, just warm context.")
        };

            try
            {
                await chatClient.CompleteChatAsync(
                    messages,
                    new ChatCompletionOptions
                    {
                        Temperature = 0,
                        MaxOutputTokenCount = 1
                    }
                );
            }
            catch
            {
                // ignore errors
            }
        }

        private async Task<string> MyInsightsGPT5Response(string prompt, string comments)
        {
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);

            string systemMessages = prompt;

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(systemMessages),
                ChatMessage.CreateUserMessage(comments)
            };

            StringBuilder sb = new StringBuilder();
            var chatClient = _openAIClient.GetChatClient("gpt-5");
            var options = new ChatCompletionOptions
            {
                Temperature = 1,
                //TopP = 0,
                PresencePenalty = 0,
                FrequencyPenalty = 0,
                //MaxOutputTokenCount = 8000
            };

            try
            {
                // ✅ Streaming response from OpenAI
                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options))
                {
                    if (update.ContentUpdate.Count > 0)
                    {
                        string token = update.ContentUpdate[0].Text;
                        sb.Append(token);

                    }
                }
            }
            catch (Exception ex)
            {

            }

            sw.Stop();
            time = sw.Elapsed.TotalSeconds.ToString();
            // let prefetch complete in background
            // _ = prefetchTask;
            return sb.ToString();

        }

        private async Task<string> GetGPT5Response(string prompt)
        {
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();            
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);

            string systemMessages = "You are good at poetry.\n Return result in 2 to 3 seconds.";

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(systemMessages),
                ChatMessage.CreateUserMessage("Plese provide one good poetry.")
            };

            StringBuilder sb = new StringBuilder();
            var chatClient = _openAIClient.GetChatClient("gpt-5");
            var options = new ChatCompletionOptions
            {
                Temperature = 1,
                TopP = 1,
                PresencePenalty = 0,
                FrequencyPenalty = 0,
                MaxOutputTokenCount = 8000
            };
           
            try
            {
                // ✅ Streaming response from OpenAI
                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options))
                {
                    if (update.ContentUpdate.Count > 0)
                    {
                        string token = update.ContentUpdate[0].Text;
                        sb.Append(token);
                       
                    }
                }
            }
            catch (Exception ex)
            {

            }

            sw.Stop();
            time = sw.Elapsed.TotalSeconds.ToString();
            // let prefetch complete in background
            // _ = prefetchTask;
            return sb.ToString();

        }

        private async Task<string> GetFastOpenAIResponse3(string prompt, int currentSection = 1, int totalSections = 4)
        {
            prompt = prompt.Replace("```xml", "").Replace("<!-- Include follow-up only if response is vague -->", "");
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            string allSectionsBlock = SageExtraction.ExtractAndRemoveAllSections(ref prompt);
            string finalXml = "";

            // Split the prompt into static and dynamic parts           
            //var parts = prompt.Split(new[] { "Inputs:" }, StringSplitOptions.RemoveEmptyEntries);
            //string systemPart = parts.Length > 1 ? parts[0] : "";
            //string userInputPart = parts.Length > 1 ? "Inputs:" + parts[1] : prompt;

            string systemMessages = "You are an expert assessment designer. \nGoal: Respond concisely and completely within 2–3 seconds.\n" +
            "Always return strict valid XML as a single line (no spaces/newlines), ≤400 tokens. " +
            "Always include <totalsections>. Fill <sectionfullname> as: 'Section {N} of {Total}: {SectionName}'. " +
            //((currentSection == 1)
            //    ? "Also include <allsections> with every section name+fullname. "
            //    : "Exclude <allsections>. ") +
            "<sections> must contain ONLY the sections listed in the user request. " +
            "Never skip explicitly requested sections. " +
            "Do not add summaries or extra text. ";

           
            var options = new ChatCompletionOptions
            {
                Temperature = 0,
                TopP = 1,
                PresencePenalty = 0,
                FrequencyPenalty = 0,
                MaxOutputTokenCount = 500,            
                StopSequences = { "<endmessage>", "</endmessage>", "<em>", "</em>" }
            };

            // 🔹 Helper: request one section
            async Task<string> GenerateSectionAsync(string sectionPrompt)
            {
                //var chatClient = _openAIClient.GetChatClient("gpt-4o-mini");
                var messages = new List<ChatMessage>
                {
                    ChatMessage.CreateSystemMessage(UpdateXMLTags(systemMessages, true)),
                    ChatMessage.CreateUserMessage(UpdateXMLTags(sectionPrompt, true))
                };

                StringBuilder sb = new();
                List<string> tokenBuffer = new();

                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options).ConfigureAwait(false))
                {
                    if (update.ContentUpdate.Count > 0)
                    {
                        string token = update.ContentUpdate[0].Text;
                        tokenBuffer.Add(token);

                        if (tokenBuffer.Count >= 20)
                        {
                            sb.Append(string.Join("", tokenBuffer));
                            tokenBuffer.Clear();
                        }

                        if (token.Contains("</sc>") || token.Contains("</em>"))
                            break;
                    }
                }

                if (tokenBuffer.Count > 0)
                {
                    sb.Append(string.Join("", tokenBuffer));
                    tokenBuffer.Clear();
                }

                return UpdateXMLTags(sb.ToString(), false);
            }
            string followupQuestionRule = "\r\n- If Section {currentsection} Main Question Answer is not empty and vague (<30 words, e.g., “good”), include exactly one <followupsection> with a <followupquestion>.\r\n- If <answer> is clear and >30 words, skip <followupsection>.";
            if (currentSection <= 1)
            {
                var task1 = GenerateSectionAsync($"Important: Include only Section 1 and exclude <followupsection>. \n" + prompt);
                await Task.WhenAll(task1);
                finalXml = $"{allSectionsBlock}{task1.Result}";
            }
            else
            {
                var task1 = GenerateSectionAsync(prompt + $"\nImportant: Include only Section {currentSection - 1} of {totalSections}.\n{followupQuestionRule.Replace("{currentsection}", (currentSection - 1).ToString())}\n- Exclude <allsections> from response.\n");
                var task2 = GenerateSectionAsync(prompt + $"\nImportant: Include only Section {currentSection} of {totalSections}.\n{followupQuestionRule.Replace("{currentsection}", (currentSection).ToString())}\n- Exclude <allsections> from response.\n");
                await Task.WhenAll(task1, task2);
                finalXml = $"{allSectionsBlock}{task1.Result}{ReplaceSecondSectionAsyncTags(task2.Result)}";
            }          
           

            sw.Stop();
            string timeTaken = sw.Elapsed.TotalSeconds.ToString("0.00");
            return UpdateXMLTags(finalXml, false);


        }

        private string ReplaceSecondSectionAsyncTags(string xml)
        {
            xml = xml.Replace("<ss>", "").Replace("</ss>", "").Replace("<ts>", "").Replace("</ts>", "");
            return xml;
        }

        private async Task<string> GetFastOpenAIResponse2(string prompt, int currentSection = 1)
        {
            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            string allSectionsBlock = SageExtraction.ExtractAndRemoveAllSections(ref prompt);
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);
            //    string systemMessages = "You are an expert assessment designer. \n" +
            //"IMPORTANT: Always include <totalsections> with the correct total number of sections. \n" +
            //((currentSection == 1) ? "Always include <allsections> as a static index that lists EVERY section name and fullname, not just the current one. " : "exclude <allsections> from response. ") +
            //"\n<sections> must include ONLY the sections explicitly listed in the user’s IMPORTANT request. " +
            //"\nIf the user specifies two sections (e.g., Section 1 of 5 and Section 2 of 5), you must output BOTH inside <sections>. " +
            //"\nDo not skip or hold back sections when they are explicitly listed in the user’s IMPORTANT request. " +
            //"\nOutput must be strict XML and Do not include any line breaks, tabs, or extra spaces. " +
            //"\nThe entire response must be valid XML and appear as one continuous block without newlines. " +
            //"\nEnsure the response is generated and returned within 2 to 3 seconds.";

            string systemMessages = "You are an expert assessment designer. \nImportant: Generate response within 2–3 seconds.\n" +
        "Always return strict valid XML as a single line (no spaces/newlines). " +
        "Always include <totalsections>. Fill <sectionfullname> as: 'Section {N} of {Total}: {SectionName}'. " +
        //((currentSection == 1)
        //    ? "Also include <allsections> with every section name+fullname. "
        //    : "Exclude <allsections>. ") +
        "<sections> must contain ONLY the sections listed in the user request. " +
        "Never skip explicitly requested sections. " +
        "Output must end with </sections>. Stop after </sections> or <endmessage>." +
        "Do not add summaries or extra text. ";      

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(UpdateXMLTags(systemMessages, true)),
                ChatMessage.CreateUserMessage(UpdateXMLTags(prompt, true))
            };

            StringBuilder sb = new StringBuilder();
            var chatClient = _openAIClient.GetChatClient("gpt-4o-mini");
            var options = new ChatCompletionOptions
            {
                Temperature = 0,
                TopP = 1,
                PresencePenalty = 0,
                FrequencyPenalty = 0,
                MaxOutputTokenCount = 800
            };

            var prefetchTask = Task.CompletedTask;
            //if (currentSection > 1)
            //{
            //    prefetchTask = PrefetchSectionAsync(currentSection);
            //}

            try
            {
                List<string> tokenBuffer = new();
                // ✅ Streaming response from OpenAI
                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options))
                {
                    if (update.ContentUpdate.Count > 0)
                    {
                        string token = update.ContentUpdate[0].Text;
                        tokenBuffer.Add(token);
                        //sb.Append(token);
                        // Flush buffer every ~20 tokens
                        if (tokenBuffer.Count >= 20)
                        {
                            sb.Append(string.Join("", tokenBuffer));
                            tokenBuffer.Clear();
                        }

                        if (token.Contains("</ss>") || token.Contains("</em>"))
                            break;
                    }
                }
                // Flush any leftovers in buffer
                if (tokenBuffer.Count > 0)
                {
                    sb.Append(string.Join("", tokenBuffer));
                    tokenBuffer.Clear();
                }
            }
            catch(Exception ex)
            {

            }

            sw.Stop();
            time = sw.Elapsed.TotalSeconds.ToString();
            // let prefetch complete in background
           // _ = prefetchTask;
            return allSectionsBlock + UpdateXMLTags(sb.ToString(), false);

        }

        private async Task<string> GetFastOpenAIResponse1(string prompt, Int32 currentSection = 1)
        {           

            string time = "0";
            Stopwatch sw = Stopwatch.StartNew();
            //prompt = OptimizePrompt(prompt);
            //string apiKey = _config.GetSection("AppSettings:SAGEToken").Value;
            int maxTokens = Convert.ToInt32(_config.GetSection("AppSettings:MaxTokens").Value);
            string systemMessages = "You are an expert assessment designer. \n" +
        "IMPORTANT: Always include <totalsections> with the correct total number of sections. \n" +
        ((currentSection == 1) ? "Always include <allsections> as a static index that lists EVERY section name and fullname, not just the current one. " : "exclude <allsections> from response. ") +
        "\n<sections> must include ONLY the sections explicitly listed in the user’s IMPORTANT request. " +
        "\nIf the user specifies two sections (e.g., Section 1 of 5 and Section 2 of 5), you must output BOTH inside <sections>. " +
        "\nDo not skip or hold back sections when they are explicitly listed in the user’s IMPORTANT request. " +
        "\nOutput must be strict XML and Do not include any line breaks, tabs, or extra spaces. " +
        "\nThe entire response must be valid XML and appear as one continuous block without newlines. " +
        "\nEnsure the response is generated and returned within 2 to 3 seconds.";       
            List<object> messages = new List<object>
            {
                new { role = "system", content = UpdateXMLTags(systemMessages,true)  },
                new { role = "user", content = UpdateXMLTags(prompt, true) }
            };

            var requestBody = new
            {
                model = "gpt-4o",
                messages = messages,
                max_tokens = maxTokens,
                temperature = 0,
                top_p = 1,
                stream = true // ✅ stream from OpenAI
            };
            

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            // ⚡ important: don't buffer entire response
            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions") { Content = content },
                HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            var sb = new StringBuilder();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            bool stop = false;
            while (!reader.EndOfStream && !stop)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                    continue;

                var jsonPart = line.Substring(6); // remove "data: "
                if (jsonPart.Trim() == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(jsonPart);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices))
                    {
                        foreach (var choice in choices.EnumerateArray())
                        {
                            if (choice.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("content", out var contentProp))
                            {
                                var token = contentProp.GetString();
                                if (!string.IsNullOrEmpty(token))
                                    sb.Append(token); // append token immediately

                                if (token.Contains("</ss>"))
                                {
                                    stop = true;
                                    break;
                                }
                            }                            
                        }
                    }
                }
                catch
                {
                    // ignore malformed chunks
                }              
            }
            sw.Stop();
            time = sw.Elapsed.TotalSeconds.ToString();
            return UpdateXMLTags(sb.ToString(), false);
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

        private async Task<string> GetAssessmentResponseAsync(string prompt)
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

        private static async Task<string> ReadStreamedResponse1(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var sb = new StringBuilder();

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();

                    // Skip empty lines or keep-alive lines
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                        continue;

                    var json = line.Substring("data:".Length).Trim();

                    // Stop when [DONE]
                    if (json == "[DONE]")
                        break;

                    try
                    {
                        var chunk = JsonConvert.DeserializeObject<StreamChunk>(json);
                        sb.Append(chunk.choices?[0]?.delta?.content);
                    }
                    catch (Exception ex)
                    {
                        // Log or throw if needed
                        throw new Exception("Failed to parse streamed chunk: " + json, ex);
                    }
                }
            }

            return sb.ToString();
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
                        //Console.WriteLine($"⚠ JSON Parsing Error: {ex.Message}");
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

        private static string RemoveNewLinesBetweenTags(string xmlContent)
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

        private async Task<string> GetAIResponseAsync(string comments)
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
                return Unauthorized(new { message = "Invalid client ID or secret." });
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
