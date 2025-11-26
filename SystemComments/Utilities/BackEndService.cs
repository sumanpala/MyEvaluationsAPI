using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

        private static List<Int16> GetAudiences()
        {
            List<Int16> audiences = new List<Int16>();
            audiences.Add(1);
            audiences.Add(7);
            audiences.Add(3);
            audiences.Add(6);
            audiences.Add(9);
            return audiences;
        }

        public static async Task<string> GetRotationMyInsightsNarrativeResponse1(
        MyInsightsRotationSummary input,
        MyInsightsRotationSummaryResponse summaryResponse,
        APIDataBaseContext _context,
        IConfiguration _config,
        OpenAIClient _openAIMyInsightsClient)
        {
            var audiences = GetAudiences();

            // Run each audience in parallel
            var audienceTasks = audiences.Select(async audienceID =>
            {
                try
                {
                    SqlParameter[] parameters =
                    {
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@AcademicYear", input.AcademicYear),
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@TargetID", audienceID)
                    };

                    var dsInsights = _context.ExecuteStoredProcedure("GetProgramCommnetsForInsights", parameters);
                    if (dsInsights == null)
                        return;

                    var dtPrompt = dsInsights.Tables[0];
                    var dtInsights = dsInsights.Tables[1];

                    if (dtPrompt.Rows.Count == 0 || dtInsights.Rows.Count == 0)
                        return;

                    // Prepare variables from dtPrompt
                    var prompt = dtPrompt.Rows[0]["APIFileContent"].ToString();
                    var summaryPrompt = dtPrompt.Rows[0]["SummaryAFIContent"].ToString();
                    var startDate = dtPrompt.Rows[0]["StartDate"].ToString();
                    var endDate = dtPrompt.Rows[0]["EndDate"].ToString();
                    var midDate = dtPrompt.Rows[0]["MidDate"].ToString();
                    var midDate1 = dtPrompt.Rows[0]["MidDate1"].ToString();

                    summaryResponse.SummaryIDs.Add(
                        new KeyValuePair<short, long>(audienceID, Convert.ToInt64(dtPrompt.Rows[0]["SummaryID"])));

                    // Replace placeholders
                    prompt = prompt.Replace("<br/>", "\n")
                                   .Replace("</br>", "\n")
                                   .Replace("<br>", "\n")
                                   .Replace("[Start Date]", startDate)
                                   .Replace("[End Date]", endDate)
                                   .Replace("[Mid Date]", midDate)
                                   .Replace("[Mid Date1]", midDate1);

                    // Distinct rotations
                    var distinctRotations = dtInsights.AsEnumerable()
                        .Select(r => r.Field<string>("Rotation"))
                        .Where(r => !string.IsNullOrEmpty(r))
                        .Distinct()
                        .OrderBy(r => r)
                        .ToList();

                    const int batchSize = 10;
                    var batchTasks = new List<Task<string>>();

                    // Process rotation batches concurrently
                    foreach (var batch in distinctRotations.Chunk(batchSize))
                    {
                        batchTasks.Add(Task.Run(async () =>
                        {
                            var sb = new StringBuilder();

                            foreach (var rotation in batch)
                            {
                                var comments = dtInsights.AsEnumerable()
                                    .Where(row => row.Field<string>("Rotation") == rotation)
                                    .Select(row => row["CommentLine"]?.ToString()?.Replace("<br/>", "\n").Replace("\r\n", "\n").Replace("\n", " ").Trim())
                                    .Where(text => !string.IsNullOrEmpty(text))
                                    .ToList();

                                if (comments.Count == 0) continue;

                                sb.AppendLine($"Rotation: {rotation}");
                                sb.AppendLine(string.Join(Environment.NewLine, comments));
                                sb.AppendLine();
                            }

                            string cleaned = Regex.Replace(
                                sb.ToString().Replace("\r\n", "\n").Replace("\n\n", "\n").Replace("<br/>", "\n"),
                                "<.*?>", string.Empty);

                            // If needed, summarize each batch here
                            // string summary = await PromptService.SummarizeMisightsRotationText4(_config, cleaned, 9000);
                            return cleaned.Trim();
                        }));
                    }

                    var summaries = await Task.WhenAll(batchTasks);

                    // Combine all summaries into one text block
                    var combinedSummaries = string.Join(Environment.NewLine + Environment.NewLine, summaries);

                    string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\"));
                    string targetFolder = Path.Combine(projectRoot, "Files", input.DepartmentID.ToString(), audienceID.ToString());
                    Directory.CreateDirectory(targetFolder);

                    // Prepare GPT prompt calls for each summarized section
                    var gptTasks = summaries.Select((summary, index) => Task.Run(async () =>
                    {
                        string newPrompt = prompt.Replace("[Rotation Comments]", summary);
                        
                        string promptFilePath = Path.Combine(targetFolder, $"Part{index + 1}Prompt.txt");
                        await File.WriteAllTextAsync(promptFilePath, newPrompt);
                        
                        return await PromptService.MyInsightsGPT5Response(_openAIMyInsightsClient, newPrompt);
                    })).ToList();

                    var jsonResults = await Task.WhenAll(gptTasks);

                    // Build JSON output
                    var finalJson = new StringBuilder();
                    finalJson.AppendLine("{");
                    finalJson.AppendLine("  \"Rotations\": [");                    

                    for (int i = 0; i < jsonResults.Length; i++)
                    {
                        string json = jsonResults[i];
                        if (string.IsNullOrWhiteSpace(json)) continue;

                        try
                        {
                            string filePath = Path.Combine(targetFolder, $"Part{i + 1}Response.txt");
                            await File.WriteAllTextAsync(filePath, json);

                            var obj = JObject.Parse(json);
                            var rotations = obj["Rotations"]?.ToString(Formatting.None);

                            if (!string.IsNullOrEmpty(rotations))
                            {
                                if (i > 0) finalJson.AppendLine(",");
                                finalJson.Append(rotations.Trim().TrimStart('[').TrimEnd(']'));
                            }
                        }
                        catch
                        {
                            // You may log exceptions if needed
                        }
                    }

                    finalJson.AppendLine("  ]");
                    finalJson.AppendLine("}");

                    // Write final JSON output
                    string responseFile = Path.Combine(targetFolder, "RotationResponse.txt");
                    await File.WriteAllTextAsync(responseFile, finalJson.ToString());

                    // ✅ Update prompt with all combined summaries before saving
                    prompt = prompt.Replace("[Rotation Comments]", combinedSummaries);

                    // Write final updated prompt to file
                    string promptFile = Path.Combine(targetFolder, "RotationPrompt.txt");
                    await File.WriteAllTextAsync(promptFile, prompt);

                    // ✅ Save both JSON and final prompt in summaryResponse
                    summaryResponse.SummaryJSONs.Add(new KeyValuePair<short, string>(audienceID, finalJson.ToString()));
                    summaryResponse.Prompts.Add(new KeyValuePair<short, string>(audienceID, prompt));
                    summaryResponse.SummaryFeedbackPrompt = summaryPrompt;
                }
                catch (Exception ex)
                {
                    // Log per audience error if needed
                    Console.WriteLine($"Error processing audience {audienceID}: {ex.Message}");
                }
            });

            await Task.WhenAll(audienceTasks);

            // Return the last or combined summary prompt
            return summaryResponse.SummaryFeedbackPrompt;
        }



        public static async Task<string> GetRotationMyInsightsNarrativeResponse2(MyInsightsRotationSummary input, MyInsightsRotationSummaryResponse summaryResponse
            , APIDataBaseContext _context, IConfiguration _config, OpenAIClient _openAIMyInsightsClient)
        {
            string prompt = "", summaryPrompt = "";
            List<Int16> audiences = GetAudiences();
            foreach (Int16 audienceID in audiences)
            {                
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@DepartmentID", input.DepartmentID),
                    new SqlParameter("@AcademicYear", input.AcademicYear),
                    new SqlParameter("@UserID", input.UserID),
                    new SqlParameter("@TargetID", audienceID)
                };

                await Task.Run(async () =>
                {
                    DataSet dsInsights = _context.ExecuteStoredProcedure("GetProgramCommnetsForInsights", parameters);
                    if (dsInsights == null) return;

                    DataTable dtPrompt = dsInsights.Tables[0];
                    DataTable dtInsights = dsInsights.Tables[1];

                    if (dtPrompt.Rows.Count > 0)
                    {
                        string startDate = dtPrompt.Rows[0]["StartDate"].ToString();
                        string endDate = dtPrompt.Rows[0]["EndDate"].ToString();
                        string midDate = dtPrompt.Rows[0]["MidDate"].ToString();
                        string midDate1 = dtPrompt.Rows[0]["MidDate1"].ToString();

                        summaryResponse.SummaryIDs.Add(new KeyValuePair<Int16, Int64>(audienceID, Convert.ToInt64(dtPrompt.Rows[0]["SummaryID"].ToString())));
                                             
                        prompt = dtPrompt.Rows[0]["APIFileContent"].ToString();
                        summaryPrompt = dtPrompt.Rows[0]["SummaryAFIContent"].ToString();
                        prompt = prompt.Replace("<br/>", "\n").Replace("</br>", "\n").Replace("<br>", "\n");
                        prompt = prompt.Replace("[Start Date]", startDate).Replace("[End Date]", endDate);
                        prompt = prompt.Replace("[Mid Date]", midDate).Replace("[Mid Date1]", midDate1);
                    }

                    if (dtInsights.Rows.Count > 0)
                    {
                        // Distinct rotations
                        List<string> distinctRotations = dtInsights.AsEnumerable()
                            .Select(r => r.Field<string>("Rotation"))
                            .Where(r => !string.IsNullOrEmpty(r))
                            .Distinct()
                            .OrderBy(r => r)
                            .ToList();

                        int batchSize = 10;
                        int totalBatches = (int)Math.Ceiling(distinctRotations.Count / (double)batchSize);

                        var batchTasks = new List<Task<string>>();

                        for (int i = 0; i < distinctRotations.Count; i += batchSize)
                        {
                            var batch = distinctRotations.Skip(i).Take(batchSize).ToList();

                            // Create a new DataTable per task to avoid cross-thread DataView filtering
                            batchTasks.Add(Task.Run(async () =>
                            {
                                var sb = new StringBuilder();

                                foreach (var rotation in batch)
                                {
                                    var comments = dtInsights.AsEnumerable()
                                        .Where(row => row.Field<string>("Rotation") == rotation)
                                        .Select(row => row["CommentLine"]?.ToString()?.Replace("\n", " ").Trim())
                                        .Where(text => !string.IsNullOrEmpty(text))
                                        .ToList();

                                    if (comments.Count > 0)
                                    {
                                        sb.AppendLine($"Rotation: {rotation}");
                                        sb.AppendLine(string.Join(Environment.NewLine, comments));
                                        sb.AppendLine();
                                    }
                                }

                                string cleaned = Regex.Replace(
                                    sb.ToString().Replace("\r\n", "\n").Replace("\n\n", "\n").Replace("<br/>", "\n"),
                                    "<.*?>", string.Empty);

                                // Summarize this batch
                                //string summary = await PromptService.SummarizeMisightsRotationText4(_config, cleaned, 9000);
                                string summary = cleaned;
                                return summary.Trim();
                            }));
                        }

                        // Run all batches in parallel safely
                        var summaries = await Task.WhenAll(batchTasks);

                        // Merge in order
                        var combined = new StringBuilder();
                        var gptTasks = new List<Task<string>>();

                        for (int i = 0; i < summaries.Length; i++)
                        {
                            string newPrompt = prompt;
                            newPrompt = newPrompt.Replace("[Rotation Comments]", summaries[i]);
                            combined.AppendLine(summaries[i]);
                            combined.AppendLine();

                            gptTasks.Add(PromptService.MyInsightsGPT5Response(_openAIMyInsightsClient, newPrompt));

                        }
                        var jsonResults = await Task.WhenAll(gptTasks);
                        // Merge into a single combined JSON
                        var finalJson = new StringBuilder();
                        finalJson.AppendLine("{");
                        finalJson.AppendLine("  \"Rotations\": [");
                        // Get your project’s root directory
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));
                        string filesRoot = Path.Combine(projectRoot, "Files");
                        string subPath = input.DepartmentID.ToString() + "/" + audienceID;

                        string targetFolder = Path.Combine(filesRoot, subPath);
                        Directory.CreateDirectory(targetFolder);

                        // Define folder inside the project (e.g., "Logs")
                        //string folderPath = Path.Combine(projectRoot, folderName);
                        string filePath = string.Empty;

                        for (int i = 0; i < jsonResults.Length; i++)
                        {
                            string json = jsonResults[i];
                            if (string.IsNullOrWhiteSpace(json))
                                continue;

                            try
                            {

                                // Full file path
                                filePath = Path.Combine(targetFolder, "Part" + (i + 1).ToString() + ".txt");

                                // Save text asynchronously
                                await File.WriteAllTextAsync(filePath, json);

                                // Parse each JSON batch and extract the "Rotations" array
                                var obj = JObject.Parse(json);
                                var rotations = obj["Rotations"]?.ToString(Formatting.None);
                                if (!string.IsNullOrEmpty(rotations))
                                {
                                    if (i > 0) finalJson.AppendLine(",");
                                    finalJson.Append(rotations.Trim().TrimStart('[').TrimEnd(']'));
                                }
                            }
                            catch (Exception ex)
                            {

                            }
                        }

                        finalJson.AppendLine("  ]");
                        finalJson.AppendLine("}");

                        // Full file path
                        filePath = Path.Combine(targetFolder, "RotationResponse.txt");

                        // Save text asynchronously
                        await File.WriteAllTextAsync(filePath, finalJson.ToString());

                        prompt = prompt.Replace("[Rotation Comments]", combined.ToString());

                        filePath = Path.Combine(targetFolder, "RotationPrompt.txt");
                        await File.WriteAllTextAsync(filePath, prompt);

                        summaryResponse.SummaryJSONs.Add(new KeyValuePair<Int16, string>(audienceID, finalJson.ToString()));
                        summaryResponse.Prompts.Add(new KeyValuePair<Int16, string>(audienceID, prompt));
                        summaryResponse.SummaryFeedbackPrompt = summaryPrompt;
                    }
                });
            }
            return summaryPrompt;
        }

        public static async Task<string> GetRotationMyInsightsNarrativeResponse(MyInsightsRotationSummary input, APIDataBaseContext _context, IConfiguration _config, OpenAIClient _openAIMyInsightsClient)
        {
            string prompt = "", summaryPrompt = "";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@DepartmentID", input.DepartmentID),
                new SqlParameter("@AcademicYear", input.AcademicYear),
                new SqlParameter("@TargetID", input.TargetID)
            };

            // Use Task.Run to ensure the method runs asynchronously
            await Task.Run(async () =>
            {
                DataSet dsInsights = _context.ExecuteStoredProcedure("GetProgramCommnetsForInsights", parameters);
                if (dsInsights == null) return;

                DataTable dtPrompt = dsInsights.Tables[0];
                DataTable dtInsights = dsInsights.Tables[1];

                if (dtPrompt.Rows.Count > 0)
                {
                    string startDate = dtPrompt.Rows[0]["StartDate"].ToString();
                    string endDate = dtPrompt.Rows[0]["EndDate"].ToString();
                    string midDate = dtPrompt.Rows[0]["MidDate"].ToString();
                    string midDate1 = dtPrompt.Rows[0]["MidDate1"].ToString();
                    //input.SummaryID = Convert.ToInt64(dtPrompt.Rows[0]["SummaryID"].ToString());
                    prompt = dtPrompt.Rows[0]["APIFileContent"].ToString();
                    summaryPrompt = dtPrompt.Rows[0]["SummaryAFIContent"].ToString();
                    prompt = prompt.Replace("<br/>", "\n").Replace("</br>", "\n").Replace("<br>", "\n");
                    prompt = prompt.Replace("[Start Date]", startDate).Replace("[End Date]", endDate);
                    prompt = prompt.Replace("[Mid Date]", midDate).Replace("[Mid Date1]", midDate1);
                }

                if (dtInsights.Rows.Count > 0)
                {
                    // Distinct rotations
                    List<string> distinctRotations = dtInsights.AsEnumerable()
                        .Select(r => r.Field<string>("Rotation"))
                        .Where(r => !string.IsNullOrEmpty(r))
                        .Distinct()
                        .OrderBy(r => r)
                        .ToList();

                    int batchSize = 10;
                    int totalBatches = (int)Math.Ceiling(distinctRotations.Count / (double)batchSize);

                    var batchTasks = new List<Task<string>>();

                    for (int i = 0; i < distinctRotations.Count; i += batchSize)
                    {
                        var batch = distinctRotations.Skip(i).Take(batchSize).ToList();

                        // Create a new DataTable per task to avoid cross-thread DataView filtering
                        batchTasks.Add(Task.Run(async () =>
                        {
                            var sb = new StringBuilder();

                            foreach (var rotation in batch)
                            {
                                var comments = dtInsights.AsEnumerable()
                                    .Where(row => row.Field<string>("Rotation") == rotation)
                                    .Select(row => row["CommentLine"]?.ToString()?.Replace("\n", " ").Trim())
                                    .Where(text => !string.IsNullOrEmpty(text))
                                    .ToList();

                                if (comments.Count > 0)
                                {
                                    sb.AppendLine($"Rotation: {rotation}");
                                    sb.AppendLine(string.Join(Environment.NewLine, comments));
                                    sb.AppendLine();
                                }
                            }

                            string cleaned = Regex.Replace(
                                sb.ToString().Replace("\r\n", "\n").Replace("\n\n", "\n").Replace("<br/>", "\n"),
                                "<.*?>", string.Empty);

                            // Summarize this batch
                            string summary = await PromptService.SummarizeMisightsRotationText4(_config, cleaned, 9000);
                            return summary.Trim();
                        }));
                    }

                    // Run all batches in parallel safely
                    var summaries = await Task.WhenAll(batchTasks);

                    // Merge in order
                    var combined = new StringBuilder();
                    var gptTasks = new List<Task<string>>();

                    for (int i = 0; i < summaries.Length; i++)
                    {
                        string newPrompt = prompt;
                        newPrompt = newPrompt.Replace("[Rotation Comments]", summaries[i]);
                        combined.AppendLine(summaries[i]);
                        combined.AppendLine();

                        gptTasks.Add(PromptService.MyInsightsGPT5Response(_openAIMyInsightsClient, newPrompt));

                    }
                    var jsonResults = await Task.WhenAll(gptTasks);
                    // Merge into a single combined JSON
                    var finalJson = new StringBuilder();
                    finalJson.AppendLine("{");
                    finalJson.AppendLine("  \"Rotations\": [");
                    // Get your project’s root directory
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string projectRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));
                    string filesRoot = Path.Combine(projectRoot, "Files");
                    string subPath = input.DepartmentID.ToString() + "/" + input.TargetID;

                    string targetFolder = Path.Combine(filesRoot, subPath);
                    Directory.CreateDirectory(targetFolder);

                    // Define folder inside the project (e.g., "Logs")
                    //string folderPath = Path.Combine(projectRoot, folderName);
                    string filePath = string.Empty;

                    for (int i = 0; i < jsonResults.Length; i++)
                    {
                        string json = jsonResults[i];
                        if (string.IsNullOrWhiteSpace(json))
                            continue;

                        try
                        {                                                 

                            // Full file path
                            filePath = Path.Combine(targetFolder, "Part" + (i + 1).ToString() + ".txt");

                            // Save text asynchronously
                            await File.WriteAllTextAsync(filePath, json);

                            // Parse each JSON batch and extract the "Rotations" array
                            var obj = JObject.Parse(json);
                            var rotations = obj["Rotations"]?.ToString(Formatting.None);
                            if (!string.IsNullOrEmpty(rotations))
                            {
                                if (i > 0) finalJson.AppendLine(",");
                                finalJson.Append(rotations.Trim().TrimStart('[').TrimEnd(']'));
                            }
                        }
                        catch (Exception ex)
                        {
                            
                        }
                    }

                    finalJson.AppendLine("  ]");
                    finalJson.AppendLine("}");

                    // Full file path
                    filePath = Path.Combine(targetFolder, "RotationResponse.txt");

                    // Save text asynchronously
                    await File.WriteAllTextAsync(filePath, finalJson.ToString());

                    prompt = prompt.Replace("[Rotation Comments]", combined.ToString());

                    filePath = Path.Combine(targetFolder, "RotationPrompt.txt");
                    await File.WriteAllTextAsync(filePath, prompt);

                    //input.Prompt = prompt;
                    //input.SummaryPrompt = summaryPrompt;
                    //input.SummaryJSON = finalJson.ToString();
                }
            });

            return prompt;
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
                            new SqlParameter("@IsDeletedRotations","0"),
                            new SqlParameter("@EvaluationID",input.EvaluationID)

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

        public static string ConvertSurveyDataToJson(DataTable dtSurvey, DataTable dtSurveyQuestions, Int16 isResident)
        {
            if (dtSurvey.Rows.Count == 0)
                return "{}";

            // Extract header-level fields (from first row)
            var academicYear = dtSurvey.Rows[0]["AcademicYear"].ToString();
            var surveyed = dtSurvey.Rows[0].Table.Columns.Contains("Surveyed") ? dtSurvey.Rows[0]["Surveyed"].ToString() : "0";
            var responded = dtSurvey.Rows[0].Table.Columns.Contains("Responded") ? dtSurvey.Rows[0]["Responded"].ToString() : "0";
            var responseRate = dtSurvey.Rows[0].Table.Columns.Contains("ResponseRate") ? dtSurvey.Rows[0]["ResponseRate"].ToString() : "0";

            var surveyList = new List<object>();

            foreach (DataRow row in dtSurveyQuestions.Rows)
            {
                if (isResident == 0 || isResident == 1)
                {
                    surveyList.Add(new
                    {
                        ReportCategory = row["ReportCategory"].ToString(),
                        QuestionText = row["QuestionText"].ToString(),
                        ProgramCompliant = SafeDecimal(row, "ProgramCompliant"),
                        SpecialtyCompliant = SafeDecimal(row, "SpecialtyCompliant"),
                        NationalCompliant = SafeDecimal(row, "NationalCompliant"),
                        ProgramMean = SafeDecimal(row, "ProgramMean"),
                        SpecialtyMean = SafeDecimal(row, "SpecialtyMean"),
                        NationalMean = SafeDecimal(row, "NationalMean")

                    });
                }
                else
                {
                    surveyList.Add(new
                    {                        
                        QuestionText = row["QuestionText"].ToString(),
                        StronglyAgree = SafeDecimal(row, "StronglyAgree"),
                        Agree = SafeDecimal(row, "Agree"),
                        Disagree = SafeDecimal(row, "Disagree"),
                        StronglyDisagree = SafeDecimal(row, "StronglyDisagree"),
                        ProgramMean = SafeDecimal(row, "ProgramMean"),                       
                        NationalMean = SafeDecimal(row, "NationalMean")

                    });
                }
            }

            var output = new
            {
                AcademicYear = academicYear,
                Surveyed = surveyed,
                Responded = responded,
                ResponseRate = responseRate,
                Survey = surveyList
            };

            return JsonConvert.SerializeObject(output, Formatting.Indented);
        }

        private static decimal SafeDecimal(DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && decimal.TryParse(row[columnName].ToString(), out var val)
                ? val
                : 0;
        }

        public static async Task<string> GetACGMESurveyImportingDataForInsights(MyInsightsSurveyRequest input, MyInsightsSurveyResponse insightResponse, APIDataBaseContext _context, OpenAIClient _openAIMyInsightsClient)
        {
            string prompt = string.Empty, response = string.Empty, stage2Prompt = string.Empty;
            bool isDataAvailable = false;
            SqlParameter[] parameters = new SqlParameter[]
                    {
                            new SqlParameter("@AcademicYear", input.AcademicYear),
                            new SqlParameter("@DepartmentID", input.DepartmentID),
                            new SqlParameter("@IsResident", input.IsResident),
                            new SqlParameter("@SurveyID", input.SurveyID),
                            new SqlParameter("@SurveyAssessmentType", input.SurveyAssessmentType)
                    };

            DataSet dsData = _context.ExecuteStoredProcedure("GetACGMESurveyImportingDataForInsights", parameters);
            if(dsData != null && dsData.Tables.Count > 0)
            {
                DataTable dtPrompts = dsData.Tables[0];
                DataView dvSurvey = new DataView(dsData.Tables[1]);
                DataView dvSurveyQuestions = new DataView(dsData.Tables[2]);
                if(dtPrompts.Rows.Count > 0)
                {
                    prompt = dtPrompts.Rows[0]["APIFileContent"].ToString();
                    stage2Prompt = dtPrompts.Rows[0]["Stage2Prompt"].ToString();
                    string programName = dtPrompts.Rows[0]["ProgramName"].ToString();                    
                    prompt = prompt.Replace("[Program Type]", programName);
                    stage2Prompt = stage2Prompt.Replace("[Program Type]", programName);

                    Int32[] years = new Int32[2];
                    years[0] = input.AcademicYear;
                    years[1] = input.AcademicYear - 1;

                    foreach(Int32 academicYear in years)
                    {
                        dvSurvey.RowFilter = $"AcademicYear={academicYear}";
                        dvSurveyQuestions.RowFilter = $"AcademicYear={academicYear}";
                        if(academicYear == input.AcademicYear && dvSurvey.Count == 0)
                        {
                            break;
                        }
                        if (dvSurvey.Count > 0)
                        {
                            string surveyJSON = ConvertSurveyDataToJson(dvSurvey.ToTable(), dvSurveyQuestions.ToTable(), input.IsResident);
                            if(surveyJSON.Length <= 2)
                            {
                                surveyJSON = string.Empty;
                            }
                            prompt = (academicYear == input.AcademicYear) ? prompt.Replace("[ACGME Survey Data]", surveyJSON) : prompt.Replace("[ACGME Previous Survey Data]", surveyJSON);
                            isDataAvailable = true;
                        }
                        else
                        {
                            prompt = (academicYear == input.AcademicYear) ? prompt.Replace("[ACGME Survey Data]", "") : prompt.Replace("[ACGME Previous Survey Data]", "");
                        }
                        
                    }

                    // Get Stage 1 Response 
                    if(isDataAvailable)
                    {
                        string systemMessage = "You are GPT-5, an expert analyst in Graduate Medical Education (GME) accreditation, survey analytics, and program evaluation reporting.\r\n\r\n" +
                            "Your role:\r\n- Interpret and execute all instructions from the user message as a Graduate Medical Education specialist assisting a Program Evaluation Committee (PEC)." +
                            "\r\n- Analyze ACGME Resident/Fellow Survey datasets to identify Performance Improvement Topics (PITs) with Year-over-Year (YoY) awareness." +
                            "\r\n- Produce structured, deterministic JSON output suitable for inclusion in PEC documentation.\r\n\r\nBehavioral directives:\r\n" +
                            "1. Follow the user’s provided rules, scales, and thresholds exactly. \r\n2. Maintain full determinism — identical inputs must yield identical outputs.\r\n" +
                            "3. Never omit or merge survey questions unless explicitly instructed.\r\n4. Do not summarize, truncate, or paraphrase user instructions.\r\n" +
                            "5. Never produce random values, placeholders, or averages not present in the data.\r\n6. Retain numeric precision exactly as given in the input (no rounding beyond input precision).\r\n" +
                            "7. Never insert narrative commentary outside the defined JSON output.\r\n8. If prior-year data are provided, perform correct YoY delta calculations using the same field names.\r\n" +
                            "9. Use only ACGME Common Program Requirements effective September 2025 or newer.\r\n10. Treat Overall Experience (“Overall Evaluation” and “Overall Opinion”) as two distinct PITs.\r\n" +
                            "11. Apply classification logic strictly:\r\n    • \"Priority\" = requires action based on thresholds or YoY decline.  \r\n    " +
                            "• \"Monitor\" = stable or above-benchmark performance requiring continued observation.\r\n\r\nOutput rules:\r\n- Always output a single JSON object named \"PEC_Output\"." +
                            "\r\n- Each array element represents one PIT.\r\n- Each PIT must include: \r\n  Severity, ReportCategory, PITTitle, PITDefinition, Frequency, Justification, and CPRReferences." +
                            "\r\n- Do not add extra keys or formatting beyond valid JSON.\r\n- Do not include tables, markdown, or natural-language explanations outside the JSON block.\r\n\r\nTone and purpose:" +
                            "\r\n- Write definitions and justifications in clear, professional PEC-report language.  \r\n- Emphasize compliance, supervision, safety, and educational quality per ACGME CPR." +
                            "\r\n\r\nIf any ambiguity arises, prefer explicit compliance with user instructions over assumptions.\r\n\r\nEnd of system message.\r\n";
                        response = await PromptService.MyInsightsGPT5Response(_openAIMyInsightsClient, prompt, systemMessage);
                        insightResponse.Part1JSON = response;
                        insightResponse.Part1Prompt = prompt;
                        stage2Prompt = stage2Prompt.Replace("[Survey Input]", response);

                        systemMessage = "You are GPT-5, a deterministic Graduate Medical Education (GME) analytics and process-improvement expert supporting Program Evaluation Committees (PECs).\r\n\r\n" +
                            "ROLE:\r\nYou analyze ACGME Resident/Fellow Survey Performance Improvement Topics (PITs) and produce PEC-ready, structured action plans.\r\nYou interpret the user’s message as containing PIT data and framework selection rules." +
                            "\r\nYou generate one JSON object per PIT—never summaries, previews, or markdown.\r\n\r\nDOMAIN EXPECTATIONS:\r\n• Apply 2025 ACGME Common Program Requirements (CPR) or newer only.\r\n" +
                            "• Exclude all references to DEI-specific accreditation elements.\r\n• Treat each PIT independently unless consolidation rules are explicitly defined.\r\n" +
                            "• Select an improvement framework—PDSA, 6-Sigma (DMAIC), or SWOT—based on PIT characteristics.\r\n• Justify framework choice clearly.\r\n• Integrate MyEvaluations ecosystem tools (Self-Assessments, SAGE Evaluations, MyInsights, Clinical Hour Tracking, MyQuiz, OLBI Burnout Inventory) where relevant for tracking or monitoring." +
                            "\r\n\r\nOUTPUT REQUIREMENTS:\r\n1. Output must be a single valid JSON object named `\"PEC_ActionPlans\"`.\r\n2. Each PIT = one element (row) inside the `\"PEC_ActionPlans\"` array.\r\n3. Each element must include the complete schema below; unused fields are blank or null.\r\n\r\nREQUIRED JSON SCHEMA:\r\n{\r\n  " +
                            "\"PEC_ActionPlans\": [\r\n    {\r\n      \"Severity\": \"Priority\" or \"Monitor\",\r\n      \"ReportCategory\": \"string\",\r\n      \"PITTitle\": \"string\",\r\n      \"PITDefinition\": \"string\",\r\n      \"Frequency\": \"integer\",\r\n      \"Justification\": \"string (include CPR references)\",\r\n     " +
                            " \"CPRReferences\": [\"string\",\"string\"],\r\n\r\n      \"ActionPlanModel\": \"PDSA\" or \"6-Sigma\" or \"SWOT\",\r\n      \"ModelJustification\": \"string\",\r\n\r\n      \"Plan\": \"string\",\r\n      \"Do\": \"string\",\r\n      \"Study\": \"string\",\r\n      \"Act\": \"string\",\r\n\r\n      " +
                            "\"Define\": \"string\",\r\n      \"Measure\": \"string\",\r\n      \"Analyze\": \"string\",\r\n      \"Improve\": \"string\",\r\n      \"Control\": \"string\",\r\n\r\n      \"Strength\": \"string\",\r\n      \"Weakness\": \"string\",\r\n      \"Opportunity\": \"string\",\r\n      \"Threat\": \"string\",\r\n\r\n      " +
                            "}\r\n  ]\r\n}\r\n\r\nCONSTRAINTS:\r\n• Produce deterministic results—identical inputs yield identical outputs.\r\n• Do not generate extra commentary, examples, or markdown.\r\n" +
                            "• Do not summarize or condense multiple PITs.\r\n• Maintain exact numeric and textual precision from the input.\r\n• Do not include placeholders or filler text (e.g., “TBD,” “example,” “...”).\r\n• Output only valid JSON (no tables, headers, or descriptive prose).\r\n• Never request clarification or confirmation.\r\n\r\nWRITING STYLE:\r\n" +
                            "• Use concise, directive, professional language suitable for PEC documentation.\r\n• Each action plan must sound implementation-ready, not conceptual.\r\n• Justifications reference ACGME CPR sections using decimal notation (e.g., “1.8.c”).\r\n• When referencing monitoring tools, describe explicit use (“Track via MyInsights dashboard after each rotation cycle.”)." +
                            "\r\n\r\nEXECUTION LOGIC:\r\n• Read and apply all rules from the user message exactly as written.\r\n• Treat the user message as containing complete instructions and input data.\r\n• Begin immediately upon receipt—no intermediate output or explanation.\r\n• End after emitting the single `\"PEC_ActionPlans\"` JSON block.\r\n";

                        response = await PromptService.MyInsightsGPT5Response(_openAIMyInsightsClient, stage2Prompt, systemMessage);
                        insightResponse.Part2JSON = response;
                        insightResponse.Part2Prompt = stage2Prompt;

                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));
                        string filesRoot = Path.Combine(projectRoot, "Files");
                        string subPath = input.DepartmentID.ToString() + "/" + input.IsResident;

                        string targetFolder = Path.Combine(filesRoot, subPath);
                        Directory.CreateDirectory(targetFolder);
                        
                        string filePath = string.Empty;

                        // Full file path
                        filePath = Path.Combine(targetFolder, "Stage1Json.txt");
                        // Save text asynchronously
                        await File.WriteAllTextAsync(filePath, insightResponse.Part1JSON);

                        filePath = Path.Combine(targetFolder, "Stage2Json.txt");
                        await File.WriteAllTextAsync(filePath, insightResponse.Part2JSON);

                        SaveSurveyInsights(_context, input, insightResponse);
                    }

                }

            }
            return response;

        }

        public static DataSet SaveSurveyInsights(APIDataBaseContext _context, MyInsightsSurveyRequest input, MyInsightsSurveyResponse insightResponse)
        {
            DataSet dsResultSet = new DataSet();
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@Academicyear", input.AcademicYear),
                        new SqlParameter("@IsResident", input.IsResident),
                        new SqlParameter("@SurveyID", input.SurveyID),
                        new SqlParameter("@SurveyAssessmentType", input.SurveyAssessmentType),
                        new SqlParameter("@Stage1Prompt", insightResponse.Part1Prompt),
                        new SqlParameter("@Stage2Prompt", insightResponse.Part2Prompt),
                        new SqlParameter("@JsonPITs", insightResponse.Part1JSON),
                        new SqlParameter("@JsonActionPlans", insightResponse.Part2JSON)

                    };
            dsResultSet = _context.ExecuteStoredProcedure("usp_SaveSurveyInsights", parameters);
            return dsResultSet;
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

        public static DataSet GetMyInsightsSummaryComments(APIDataBaseContext _context, MyInsightsSummary input)
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

        public static DataSet InsertDepartmentalSummaryFromJson(APIDataBaseContext _context, MyInsightsSummary input, MyInsightsResponse sumamryResponse)
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

        public static DataSet SaveMyInsightsFromJson(APIDataBaseContext _context, MyInsightsRotationSummary input, MyInsightsRotationSummaryResponse response)
        {
            DataSet dsResultSet = new DataSet();
            SqlParameter[] parameters = new SqlParameter[]
                    {
                        new SqlParameter("@ID", response.SummaryID),                        
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@DepartmentID", input.DepartmentID),
                        new SqlParameter("@TargetID", input.TargetID),
                        new SqlParameter("@Academicyear", input.AcademicYear),                       
                        new SqlParameter("@Json", response.SummaryJSON),                       
                        new SqlParameter("@Prompt", response.Prompt)                      

                    };

            dsResultSet = _context.ExecuteStoredProcedure("usp_SaveMyInsightsFromJson", parameters);
            return dsResultSet;
        }

        public static DataSet SaveDepartmentalSummaryFromJson(APIDataBaseContext _context, MyInsightsRotationSummary input, MyInsightsRotationSummaryResponse response)
        {
            DataSet dsResultSet = new DataSet();
            SqlParameter[] parameters = new SqlParameter[]
                    {                        
                        new SqlParameter("@UserID", input.UserID),
                        new SqlParameter("@DepartmentID", input.DepartmentID),                        
                        new SqlParameter("@Academicyear", input.AcademicYear),
                        new SqlParameter("@Json", response.SummaryFeedbackJSON),
                        new SqlParameter("@Prompt", response.SummaryFeedbackPrompt)

                    };

            dsResultSet = _context.ExecuteStoredProcedure("usp_SaveDepartmentalSummaryFromJson", parameters);
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
