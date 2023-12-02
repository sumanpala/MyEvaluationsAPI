using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
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
using Newtonsoft.Json;

namespace SystemComments.Controllers
{
    [Authorize]
    [Route("api/TaskDetails")]
    [ApiController]
    public class TaskResponseController : ControllerBase
    {
        // GET: TaskResponseController
        private readonly APIDataBaseContext _context;
        private readonly IJwtAuth jwtAuth;
        private readonly IConfiguration _config;
        private readonly ILogger<TaskResponseController> _logger;

        public TaskResponseController(APIDataBaseContext context, IJwtAuth jwtAuth, IConfiguration config, ILogger<TaskResponseController> logger)
        {
            _context = context;
            this.jwtAuth = jwtAuth;
            _config = config;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskResponse>>> GetTaskResponse()
        {
            return await _context.TaskResponse.ToListAsync();
        }

        // GET: api/TaskResponse/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TaskResponse>> GetTaskResponse(string id)
        {
            var taskResponse = await _context.TaskResponse.FindAsync(id);

            if (taskResponse == null)
            {
                return NotFound();
            }

            return taskResponse;
        }

        // PUT: api/TaskResponse/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTaskResponse(string id, TaskResponse taskResponse)
        {          
            _context.Entry(taskResponse).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return NotFound();
            }

            return NoContent();
        }

        [HttpPost]
        [ActionName("TaskDetails")]       
        [Authorize]
        public ActionResult<string> GetTaskResponse([FromBody] TaskRequest input)
        {
            string errorMessages = ValidationMessages(input);
            string response = "";            
            TaskResponse taskResponse = new TaskResponse();
            List<TaskResponse> lisResponse = new List<TaskResponse>();
            try
            {
                if (errorMessages.Length > 0)
                {
                    response = "[{\"error\": \""+ errorMessages +"\"}]";
                    taskResponse.result = response;
                }
                else
                {

                    var dataSet = new DataSet();

                    //string StoredProc = "exec ExportMedcomTaskLogs " +
                    //            "@DepartmentName = '" + input.DepartmentName + "'," +
                    //            "@StartDate = '" + input.StartDate + "'," +
                    //            "@EndDate = '" + input.EndDate + "'," +
                    //            "@StartIndex = '" + input.StartIndex + "'," +
                    //            "@PageSize = '" + input.PageSize + "'," +
                    //            "@EDIPI = '" + input.EDIPI;
                    //_context.Database.ExecuteSqlRaw("ExportMedcomTaskLogs {0},{1},{2},{3},{4},{5}"
                    //        , input.DepartmentName, input.StartDate, input.EndDate, input.StartIndex, input.PageSize, input.EDIPI);

                    string myDbConnectionString = _config.GetConnectionString("MyEvalsConnectionString");
                    DataSet ds = new DataSet();
                    using (SqlConnection Con = new SqlConnection(myDbConnectionString))
                    {
                        SqlCommand commandInfo = new SqlCommand();
                        commandInfo.Connection = Con;
                        commandInfo.CommandType = CommandType.StoredProcedure;
                        commandInfo.CommandText = "ExportMedcomTaskLogs";
                        commandInfo.Parameters.AddWithValue("DepartmentName", input.DepartmentName);
                        commandInfo.Parameters.AddWithValue("StartDate", input.StartDate);
                        commandInfo.Parameters.AddWithValue("EndDate", input.EndDate);
                        commandInfo.Parameters.AddWithValue("StartIndex", input.StartIndex);
                        commandInfo.Parameters.AddWithValue("PageSize", input.PageSize);
                        commandInfo.Parameters.AddWithValue("EDIPI", input.EDIPI);
                        commandInfo.CommandTimeout = 0;
                        Con.Open();
                        SqlDataAdapter da = new SqlDataAdapter(commandInfo);
                        da.Fill(ds);
                        Con.Close();
                    }
                    if (ds != null && ds.Tables.Count > 0)
                    {
                        List<string> removedColumns = new List<string>();
                        DataTable dtData = ds.Tables[0];
                        DataView dvData = new DataView(dtData);
                        foreach(DataColumn dcColumn in dtData.Columns)
                        {
                            dvData.RowFilter = "LEN([" + dcColumn.ColumnName + "]) > 0 OR LEN(ISNULL([" + dcColumn.ColumnName + "],'')) > 0";
                            if(dvData.Count == 0)
                            {
                                removedColumns.Add(dcColumn.ColumnName);
                            }
                        }
                        foreach(string columnName in removedColumns)
                        {
                            if(dtData.Columns.Contains(columnName))
                            {
                                dtData.Columns.Remove(columnName);
                            }
                        }
                        response = DataTableToJSONWithJSONNet(dtData);                      
                        //taskResponse.result = resJSON;
                    }

                    //using (var command = _context.Database.GetDbConnection().CreateCommand())
                    //{
                    //    if (command.Connection.State != ConnectionState.Open)
                    //    {
                    //        command.Connection.Open();
                    //    }
                    //    command.CommandType = CommandType.StoredProcedure;
                    //    command.Parameters.Add(new SqlParameter("DepartmentName", input.DepartmentName));
                    //    command.Parameters.Add(new SqlParameter("StartDate", input.StartDate));
                    //    command.Parameters.Add(new SqlParameter("EndDate", input.EndDate));
                    //    command.Parameters.Add(new SqlParameter("StartIndex", input.StartIndex));
                    //    command.Parameters.Add(new SqlParameter("PageSize", input.PageSize));
                    //    command.Parameters.Add(new SqlParameter("EDIPI", input.EDIPI));
                    //     command.ExecuteReader();
                    //    command.Connection.Close();
                    //}               
                }
            }
            catch(Exception ex)
            {
                response = "[{\"error\": \"" + ex.Message + "\"}]";
            }
            //lisResponse.Add(taskResponse);
            return response;
        }

        private string DataTableToJSONWithJSONNet(DataTable table)
        {
            try
            {
                string JSONString = string.Empty;
                JSONString = JsonConvert.SerializeObject(table);
                return JSONString;
            }
            catch(Exception ex)
            {
                return ex.Message;
            }
        }

        private string ValidationMessages(TaskRequest input)
        {
            string errorMessages = "";
            if(input.DepartmentName.Trim().Length == 0)
            {
                errorMessages += "Department Name is missed in the request. ";
            }
            if (input.StartDate.Trim().Length == 0)
            {
                errorMessages += "Start Date is missed in the request. ";
            }
            else
            {
                if(!IsValidDate(input.StartDate))
                {
                    errorMessages += "Start Date is invalid. ";
                }
            }
            if (input.EndDate.Trim().Length == 0)
            {
                errorMessages += "End Date is missed in the request. ";
            }
            else
            {
                if (!IsValidDate(input.EndDate))
                {
                    errorMessages += "End Date is invalid. ";
                }
            }
            if(errorMessages.Length == 0)
            {
                var diffOfDates = Convert.ToDateTime(input.EndDate) - Convert.ToDateTime(input.StartDate);
                if(diffOfDates.Days < 0)
                {
                    errorMessages += "End Date must be greater than Start Date. ";
                }
            }
            return errorMessages;

        }

        private bool IsValidDate(string inputDate)
        {
            bool valid = true;
            try
            {
                DateTime parsed;
                valid = DateTime.TryParseExact(inputDate, "MM/dd/yyyy",
                                                    CultureInfo.InvariantCulture,
                                                    DateTimeStyles.None,
                                                    out parsed);
            }
            catch(Exception ex)
            {
                valid = false;
            }
            return valid;
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
    }
}
