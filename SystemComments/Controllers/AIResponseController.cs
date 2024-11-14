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
                    AIResponse response = SendCustomComments(objComments);
                    aiResponse = response.OutputResponse;
                }
                else
                {
                    comments = GetComments(input);
                    aiResponse = GetChatGptResponse(comments);
                }
               
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
                            aiComments = aiComments.Replace("```html", "");
                        }
                    }
                    string StoredProc = "exec InsertArtificialIntelligenceResponse " +
                        "@InputPrompt = '" + input.InputPrompt + "'," +
                        "@Output = '" + aiResponse + "'";
                    //return await _context.output.ToListAsync();
                    comments = comments.Replace("\n", "<br/>");
                    comments = comments.Replace("\r\n", "<br/>");
                    aiSavedResponse = await _context.AIResponse.FromSqlRaw("InsertArtificialIntelligenceResponse {0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}"
                        ,input.CreatedBy, input.DepartmentID, input.InputPrompt, aiResponse, aiComments, input.UserID, input.DateRange, input.SearchCriteria, input.AIResponseID, "2", comments, input.IsNPV).ToListAsync();
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
                       ) ;

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
               

        private string GetChatGptResponse(string comments)
        {
            try
            {                
                string aiKey = _config.GetSection("AppSettings:AIToken").Value;
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
                        Temperature = 0.7f,
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

        [HttpPost("sendcustomcomments")]
        [Authorize]        
        public AIResponse SendCustomComments(Comments comments)
        {
            List<AIResponse> aiSavedResponse = new List<AIResponse>();
            try
            {
                string response = GetChatGptResponse(comments.InputComments);
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
