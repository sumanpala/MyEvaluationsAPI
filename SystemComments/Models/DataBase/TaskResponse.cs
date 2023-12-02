using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SystemComments.Models.DataBase
{
    public class TaskResponse
    {
        [Key]
      public Int64 TaskResponseID { get; set; }
       public string result { get; set; }      

       
    }

}
