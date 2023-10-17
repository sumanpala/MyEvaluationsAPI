using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SystemComments.Models.DataBase;

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
        public async Task<ActionResult<IEnumerable<AIResponse>>> SaveAIRequest(AIRequest input)
        {
            List<AIResponse> aiSavedResponse = new List<AIResponse>();
            try
            {
                string aiResponse = GetChatGptResponse(input);
                string aiComments = "";
                if(aiResponse.Length > 0)
                {
                    var objResponse = JToken.Parse(aiResponse);
                    JArray objChoices = (JArray)objResponse["choices"];
                    if(objChoices.Count() > 0)
                    {
                        JObject objMessages = (JObject)objChoices[0]["message"];
                        if (objMessages.Count > 0)
                        {
                            aiComments = objMessages["content"].ToString();
                        }
                    }
                    string StoredProc = "exec InsertArtificialIntelligenceResponse " +
                        "@InputPrompt = '" + input.InputPrompt + "'," +
                        "@Output = '" + aiResponse + "'";
                    //return await _context.output.ToListAsync();
                    aiSavedResponse = await _context.AIResponse.FromSqlRaw("InsertArtificialIntelligenceResponse {0},{1},{2},{3},{4},{5},{6},{7},{8},{9}"
                        ,input.CreatedBy, input.DepartmentID, input.InputPrompt, aiResponse, aiComments, input.UserID, input.DateRange, input.SearchCriteria, "", "2").ToListAsync();
                }                
                
                //return await _context.AIResponse.FromSqlRaw("InsertArtificialIntelligenceResponse {0},{1}", input.InputPrompt, aiResponse).ToListAsync();

            }
            catch(Exception ex)
            {
                AIResponse aiErrorResponse = new AIResponse();
                aiErrorResponse.AIResponseID = "";
                aiErrorResponse.CreatedDate = DateTime.Now;
                aiErrorResponse.InputPrompt = "";
                aiErrorResponse.OutputResponse = ex.Message;
                aiSavedResponse.Add(aiErrorResponse);
            }
            return aiSavedResponse;
        }

        private string GetChatGptResponse(AIRequest input)
        {
            try
            {                
                string aiKey = _config.GetSection("AppSettings:AIToken").Value;
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiKey);
                Int64 attemptNumber = input.AttemptNumber;
                string inputJSON = input.InputPrompt;
                string aiResponse = "";
                try
                {
                    var objUsers = JToken.Parse(inputJSON);
                    string userID = "0";
                    string userName = "", dateRange = "", comments = "";
                    string prompt_initial = "You are an expert medical educator. Consider the data from {} listed in chronological order. These are comments from different evaluators and demonstrate the resident's performance over time. \nConsider the performance during the initial months and compare to their performance during the latter months.  Provide a comparison of the initial performance to the most recent performance, and detail a trend in the performance.\nAssume the resident has multiple opportunities to improve and grow in that period. Analyze the comments to demonstrate a trend in their performance.\nPlease provide the resident with detailed narrative summaries of their performance.\nExclude specific names of people. \nSeparate each narrative summary by the six core ACGME competencies and provide an 'Overall MyInsights' section to summarize all their strengths and weaknesses.\nPlease sort the competency headings into the following order: Patient Care, Medical Knowledge, System-Based Practices, Practice-Based Learning & Improvement, Professionalism, and Interpersonal & Communication Skills.\nPhrase the responses to the resident but do not use their name. Do not refer to them by name." + dateRange;
                    string prompt_final = "You are an expert medical educator. Consider summary comments listed by ACGME core competencies from the period {}, followed by comments from different evaluators for the period 07/01/2022 - 04/13/2023 listed in chronological order. \nConsider the summary comments during the initial period and compare to their performance during the latter period.  Provide a comparison of the initial performance to the most recent performance, and detail a trend in the performance.\nAssume the resident has multiple opportunities to improve and grow in that period. Analyze the comments to demonstrate a trend in their performance. Please provide the resident with detailed narrative summaries of their performance.\nSeparate each narrative summary by the six core ACGME competencies and provide an 'Overall MyInsights' section to summarize all their strengths and weaknesses.\nPlease sort the competency headings into the following order: Patient Care, Medical Knowledge, System-Based Practices, Practice-Based Learning & Improvement, Professionalism, and Interpersonal & Communication Skills.\nPhrase the responses to the resident but do not use their name. Do not refer to them by name." + dateRange;
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

                        if (comments.Length > 0)
                        {
                            comments = ((attemptNumber == 1) ? prompt_initial : prompt_final) + "\n\nComments:\n" + comments;
                            var request = new OpenAIRequest
                            {
                                //Model = "text-davinci-002",
                                Model = "gpt-3.5-turbo",
                                //Prompt = "dog types",
                                Temperature = 0.7f,
                                //MaxTokens = 4000
                                //Messages = "[{\"role\":\"system\",\"content\":\"You are helpful assistant\"},{\"role\":\"user\",\"content\":\"You will never use people name when responding and only use the workd 'Resident' instead of people name\"}]"
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
                                             Content = "You will never use people name when responding and only use the workd 'Resident' instead of people name"
                                        },
                                        new RequestMessage()
                                        {
                                             Role = "user",
                                             Content = comments
                                        }
                               };

                            var json = JsonSerializer.Serialize(request);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                            var resjson = response.Result.Content.ReadAsStringAsync();
                            aiResponse = resjson.Result;
                        }
                        else
                        {
                            throw new System.Exception("Comments are not available.");
                        }
                        //foreach (var objUser in objUsers)
                        //{
                            
                        //}

                    }
                    else
                    {
                        throw new System.Exception("Comments are not available.");
                    }

                }
                catch (Exception jex)
                {
                    //Exception in parsing json
                    _logger.LogError(jex, "An error occurred while making the OpenAI API request");
                    return "An error occurred:";

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

        private bool AIResponseExists(string id)
        {
            return _context.AIResponse.Any(e => e.AIResponseID.ToString() == id);
        }
    }
}
