using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SystemComments.Models.DataBase
{
    public class AIResponse
    {
        [Key]
        public string AIResponseID { get; set; }
        public string InputPrompt { get; set; }
        public string OutputResponse { get; set; }
        public DateTime CreatedDate { get; set; }

        public string AIComments { get; set; }
        public Int64 CreatedBy { get; set; }
        public Int64 ModifiedBy { get; set; }
        public DateTime ModifiedDate { get; set; }
        public Int64 UserID { get; set; }
        public string DateRange { get; set; }

        public Int64 DepartmentID { get; set; }

        public string SearchCriteria { get; set; }

        public string Error { get; set; }

    }
    public class SAGEResponse
    {
        [Key]
        public Int64 EvaluationID { get; set; }
        [JsonIgnore]
        public string AIPrompt { get; set; }
        public string ResponseJSON { get; set; }

    }

    public class APEResponse
    {       
        public string PITJSON { get; set; }
        public string AFIJSON { get; set; }
        public string AFIProgramJSON { get; set; }

    }

    public class Choice
    {
        public string text { get; set; }
        public int index { get; set; }
        public object logprobs { get; set; }
        public string finish_reason { get; set; }
    }
    public class Root
    {
        public string id { get; set; }
        public string @object { get; set; }
        public int created { get; set; }
        public string model { get; set; }
        public List<Choice> choices { get; set; }
        public Usage usage { get; set; }
    }
    public class Usage
    {
        public int prompt_tokens { get; set; }
        public int completion_tokens { get; set; }
        public int total_tokens { get; set; }
    }
    public class OpenAIChoice
    {
        public string text { get; set; }
        public float probability { get; set; }
        public float[] logprobs { get; set; }
        public int[] finish_reason { get; set; }
    }
    public class OpenAIRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        //[JsonPropertyName("prompt")]
        //public string Prompt { get; set; }

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("messages")]
        public RequestMessage[] Messages { get; set; }

    }
    public class RequestMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }
        [JsonPropertyName("content")]
        public string Content { get; set; }
    }
    public class OpenAIErrorResponse
    {
        [JsonPropertyName("error")]
        public OpenAIError Error { get; set; }
    }
    public class OpenAIError
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("param")]
        public string Param { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }
    }
    public class StreamChunk
    {
        public List<Choice> choices { get; set; }

        public class Choice
        {
            public Delta delta { get; set; }
        }

        public class Delta
        {
            public string content { get; set; }
        }
    }
    public class CompetencyCategory
    {
        public string PrimaryACGMECompetencyOrCategory { get; set; }
        public List<PIT> PITs { get; set; }
    }

    public class PIT
    {
        public string PITTitle { get; set; }
        public string PITDefinition { get; set; }
        public int Frequency { get; set; }
        public string JustificationWithCPRReferences { get; set; }
        public List<string> CPRReferences { get; set; }
    }

    public class TimeHistory
    {
        public double TotalSeconds { get; set; } = 0;
        public double HistorySeconds { get; set; } = 0;
        public double PromptDBSeconds { get; set; } = 0;

        public double AIResponseSeconds { get; set; } = 0;

        public int ApiAttempts { get; set; } = 1;
    }
}
