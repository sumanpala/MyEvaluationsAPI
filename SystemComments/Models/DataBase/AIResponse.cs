using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
namespace SystemComments.Models.DataBase
{
    public class AIResponse
    {
        [Key]
        public string AIResponseID { get; set; }
        public string InputPrompt { get; set; }
        public string OutputResponse { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
