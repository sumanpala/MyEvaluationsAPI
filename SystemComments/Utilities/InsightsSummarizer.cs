using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

public static class InsightsSummarizer
{
    // Tune based on your model
    private const int MaxBytesPerChunk = 350_000; // ~100k tokens for gpt-4o
    private const int MaxOutputTokens = 8000;
    private static readonly SemaphoreSlim _gptSemaphore = new SemaphoreSlim(5); // Max concurrent GPT calls

    public static async Task<string> GetMyInsightsComments(Int16 isFaculty, DataSet dsComments, OpenAIClient _client)
    {
        var globalSb = new StringBuilder();
        var summarizationTasks = new List<Task<(string periodLevelKey, string summary)>>();

        try
        {
            if (dsComments?.Tables.Count > 0)
            {
                DataTable dtComments = dsComments.Tables[0];

                // Distinct periods
                var distinctPeriods = dtComments.AsEnumerable()
                    .Select(r => new
                    {
                        PeriodNum = r.Field<Int16>("PeriodNum"),
                        StartDate = r.Field<string>("StartDate"),
                        EndDate = r.Field<string>("EndDate")
                    })
                    .Distinct()
                    .OrderBy(x => x.PeriodNum)
                    .ToList();

                foreach (var objPeriod in distinctPeriods)
                {
                    int periodNum = objPeriod.PeriodNum;
                    string startDate = objPeriod.StartDate;
                    string endDate = objPeriod.EndDate;

                    var distinctTrainingLevels = dtComments.AsEnumerable()
                        .Where(r => r.Field<Int16>("PeriodNum") == periodNum)
                        .Select(r => r.Field<string>("TrainingLevel"))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    foreach (var trainingLevel in distinctTrainingLevels)
                    {
                        // Build the Training Level text block
                        var sbLevel = new StringBuilder();
                        sbLevel.AppendFormat("Period {0}: ({1}-{2})\n", periodNum, startDate, endDate);
                        sbLevel.AppendFormat("\t\t{1}: {0}\n", trainingLevel, (isFaculty == 0) ? "Training Level" : "Speciality");

                        var filteredUsers = dtComments.AsEnumerable()
                            .Where(r => r.Field<Int16>("PeriodNum") == periodNum &&
                                        r.Field<string>("TrainingLevel") == trainingLevel &&
                                        r.Field<string>("StartDate") == startDate &&
                                        r.Field<string>("EndDate") == endDate)
                            .Select(r => r.Field<long>("UserID"))
                            .Distinct()
                            .ToList();

                        foreach (var userID in filteredUsers)
                        {
                            sbLevel.AppendFormat("\t\tUserID: {0}\n", userID);
                            sbLevel.Append("\t\tNarrative MyInsights:\n");

                            var filteredRows = dtComments.AsEnumerable()
                                .Where(r => r.Field<Int16>("PeriodNum") == periodNum &&
                                            r.Field<string>("TrainingLevel") == trainingLevel &&
                                            r.Field<string>("StartDate") == startDate &&
                                            r.Field<string>("EndDate") == endDate &&
                                            r.Field<long>("UserID") == userID);

                            if (filteredRows.Any())
                            {
                                int idx = 1;
                                foreach (var dr in filteredRows)
                                {
                                    sbLevel.AppendFormat("\t\t{0}. {1}: {2}\n",
                                        idx++,
                                        dr["CompetencyName"].ToString(),
                                        RemoveHtmlTags(dr["Comments"].ToString()));
                                }
                            }
                            sbLevel.AppendLine();
                        }

                        // Split into safe chunks
                        var chunks = SplitIntoChunks(sbLevel.ToString(), MaxBytesPerChunk);

                        foreach (var chunk in chunks)
                        {
                            var key = $"Period {periodNum} - {trainingLevel}";
                            summarizationTasks.Add(SummarizeChunkAsync(chunk, _client, key));
                        }
                    }
                }
            }

            // ✅ Run all GPT summarizations concurrently
            var summaries = await Task.WhenAll(summarizationTasks);

            // ✅ Merge back in order of period/training level
            foreach (var group in summaries.GroupBy(x => x.periodLevelKey))
            {
                globalSb.AppendLine($"===== {group.Key} =====");
                foreach (var item in group)
                {
                    if (!string.IsNullOrWhiteSpace(item.summary))
                        globalSb.AppendLine(item.summary);
                }
                globalSb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in GetMyInsightsComments: {ex.Message}");
        }

        return globalSb.ToString();
    }

    private static async Task<(string periodLevelKey, string summary)> SummarizeChunkAsync(
        string userComments,
        OpenAIClient _client,
        string key)
    {       

        await _gptSemaphore.WaitAsync(); // concurrency control
        try
        {
            var chatClient = _client.GetChatClient("gpt-4.1");

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(
                @"You are a data compression assistant for Graduate Medical Education (GME) evaluations.
                Summarize and compress long trainee feedback **without losing context or structure**.

                Follow these rules precisely:
                1. Preserve the exact format and indentation:                   
                       UserID: [UserID]
                           Narrative MyInsights:
                               1. [Competency Name]: [Summarized Comment]
                2. Summarize each comment in 1–3 sentences while keeping the tone and meaning.
                3. Do not merge or remove trainees.
                4. Maintain numbering, indentation, and structure.
                5. Output plain text only (no JSON or Markdown)."),
                ChatMessage.CreateUserMessage(userComments)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 1,
                TopP = 1,
                PresencePenalty = 0,
                FrequencyPenalty = 0               
                //MaxOutputTokenCount = MaxOutputTokens
                
            };

            var response = await chatClient.CompleteChatAsync(
                messages,
                options
                );
            
            string text = response.Value.Content[0].Text;
            return (key, text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GPT summarization error [{key}]: {ex.Message}");
            return (key, string.Empty);
        }
        finally
        {
            _gptSemaphore.Release();
        }
    }

    private static List<string> SplitIntoChunks(string text, int maxBytes)
    {
        var chunks = new List<string>();
        var sections = text.Split(new[] { "\n\t\tUserID:" }, StringSplitOptions.None);

        var sb = new StringBuilder();
        int currentBytes = 0;

        for (int i = 0; i < sections.Length; i++)
        {
            string section = (i == 0 ? sections[i] : "\n\t\tUserID:" + sections[i]);
            int sectionBytes = Encoding.UTF8.GetByteCount(section);

            // If adding this section exceeds the maxBytes, flush the chunk
            if (currentBytes + sectionBytes > maxBytes && sb.Length > 0)
            {
                chunks.Add(sb.ToString());
                sb.Clear();
                currentBytes = 0;
            }

            sb.Append(section);
            currentBytes += sectionBytes;
        }

        // Add the last chunk if anything remains
        if (sb.Length > 0)
            chunks.Add(sb.ToString());

        return chunks;
    }


    //private static List<string> SplitIntoChunks(string text, int maxBytes)
    //{
    //    var chunks = new List<string>();
    //    var bytes = Encoding.UTF8.GetBytes(text);
    //    int start = 0;

    //    while (start < bytes.Length)
    //    {
    //        int length = Math.Min(maxBytes, bytes.Length - start);
    //        var chunkBytes = new byte[length];
    //        Array.Copy(bytes, start, chunkBytes, 0, length);
    //        chunks.Add(Encoding.UTF8.GetString(chunkBytes));
    //        start += length;
    //    }

    //    return chunks;
    //}

    private static string RemoveHtmlTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty);
    }
}
