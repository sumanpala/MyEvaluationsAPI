using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using SystemComments.Models.DataBase;

namespace SystemComments.Utilities
{
    public class SageExtraction
    {
        static int idCounter = 1;

        public static string ExtractData(string airesponse)
        {           
            airesponse = Regex.Replace(airesponse, @"\t|\n|\r", "");
            airesponse = Regex.Replace(airesponse, @"<(/?)(\w+)\s+(\w+)>", "<$1$2_$3>");
            airesponse = Regex.Replace(airesponse, @"[^\x09\x0A\x0D\x20-\uD7FF\uE000-\uFFFD]", "");
            airesponse = SanitizeXml(airesponse);
            string wrappedXml = "<root>" + airesponse + "</root>";
            XDocument xmlDoc = XDocument.Parse(wrappedXml);
            return ConvertXmlToJson(xmlDoc);
        }

        public static string UpdateRequestJSON(string aiJSON, string inputJSON)
        {
            string updatedJSON = "";
            JArray? sectionsArray = null;
            try
            {
                if (inputJSON.Length <= 2)
                {
                    updatedJSON = aiJSON;
                }
                else
                {
                    JObject jsonObject = JObject.Parse(aiJSON);
                    JObject objInputObject = JObject.Parse(inputJSON);

                    JArray? allSectionsFromJson1 = objInputObject["allsections"] as JArray;

                    // Check if "allsections" in JSON2 is missing or empty
                    JArray? allSectionsFromJson2 = jsonObject["allsections"] as JArray;
                    if (allSectionsFromJson1 == null || allSectionsFromJson1.Count == 0)
                    {
                        // Update JSON2 with "allsections" from JSON1
                        objInputObject["allsections"] = allSectionsFromJson2 ?? new JArray();
                    }                    
                    if (objInputObject != null)
                    {
                        sectionsArray = objInputObject["sections"] as JArray;
                    }                    

                    Int32 sectionIndex = 0;
                    foreach (var section in jsonObject["sections"])
                    {                        
                        if (sectionsArray != null && sectionsArray.Count > 0 && sectionsArray.Count > sectionIndex)
                        {
                            JObject tempSectionArray = sectionsArray[sectionIndex] as JObject;
                            Int32 mainSectionIndex = 0;
                            foreach (var mainSection in section["mainsection"])
                            {
                               JArray mainSectionArray = tempSectionArray?["mainsection"] as JArray;
                               if(mainSectionArray == null)
                                {
                                    tempSectionArray.Add(mainSectionArray);
                                }
                                mainSectionIndex++;
                            }
                            // add followup section if not available
                            var followupToken = tempSectionArray?["followupsections"];
                            if (section["followupsections"] != null)
                            {
                                if (followupToken != null)
                                {                                    
                                    if (followupToken is JArray followSectionArray)
                                    {
                                        List<JToken> newItems = new List<JToken>();
                                        foreach (var followSection in section["followupsections"])
                                        {
                                            if (followSectionArray != null && followSectionArray.Count > 0)
                                            {
                                                var followup = followSectionArray.Where(f => f["question"]?.ToString().Length > 0)
                                                .FirstOrDefault(f => f["question"]?.ToString() == followSection["question"].ToString());
                                                if (followup == null)
                                                {
                                                    newItems.Add(followSection);
                                                }
                                            }
                                        }
                                        foreach (var newItem in newItems)
                                        {
                                            followSectionArray.Add(newItem);
                                        }
                                    }
                                }
                                else
                                {
                                    tempSectionArray.Add(section["followupsections"]);
                                }                                
                                                             
                            }

                        }
                        else
                        {
                            sectionsArray.Add(section);
                        }
                        sectionIndex++;
                    }

                    var endMessage = jsonObject["endmessage"];
                    if (endMessage != null)
                    {
                        objInputObject["endmessage"] = endMessage;
                    }

                    if (objInputObject != null)
                    {
                        updatedJSON = JsonConvert.SerializeObject(objInputObject, Formatting.None);
                    }
                    else
                    {
                        updatedJSON = inputJSON;
                    }
                }                
                return updatedJSON;
            }
            catch(Exception ex)
            {
                return inputJSON;
            }
        }

        public static string UpdateJSONQuestionIDs(DataTable dtQuestions, string json)
        {
            try
            {
                JObject jsonObject = JObject.Parse(json);

                // Update JSON with question IDs from DataTable
                jsonObject = UpdateQuestionIDs(jsonObject, dtQuestions);

                // Convert back to JSON string                
                string updatedJson = JsonConvert.SerializeObject(jsonObject, Formatting.None);
                int sectionCount = jsonObject["sections"]?.Count() ?? 0;                
                return updatedJson;
            }
            catch(Exception ex)
            {
                return json;
            }
        }

        public static string RemoveUnNecessaryJSONTags(string html)
        {
            string replacedHtml = html;
            var regex = new Regex(@"<[hH][1-9][^>]*>", RegexOptions.Compiled | RegexOptions.Multiline);
            replacedHtml = regex.Replace(replacedHtml, "");
            regex = new Regex(@"</[hH][1-9][^>]*>", RegexOptions.Compiled | RegexOptions.Multiline);
            replacedHtml = regex.Replace(replacedHtml, "");
            replacedHtml = replacedHtml.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace("\"", "#DQuote#");
            replacedHtml = System.Net.WebUtility.HtmlDecode(replacedHtml);
            replacedHtml = Regex.Replace(replacedHtml, "<.*?>", String.Empty);
            replacedHtml = replacedHtml.Replace("\"", "#DQuote#");
            replacedHtml = replacedHtml.Replace("\\", "");
            replacedHtml = replacedHtml.Replace("\n", "\\n")
                            .Replace("\r", "\\r")
                            .Replace("\t", "\\t");
            return replacedHtml;
        }

        static JObject UpdateQuestionIDs(JObject json, DataTable dt)
        {
            try
            {               

                Int16 sectionIndex =0;              
                
                // Get all question fields
                foreach (var section in json["sections"])
                {                    
                    // Update Main Questions
                    foreach (var mainSection in section["mainsection"])
                    {
                        UpdateID(mainSection, "mainquestion", dt);

                        if (mainSection?["guide"]?["guidequestions"] is JArray guideQuestions && guideQuestions.Count > 0)
                        {
                            Int16 questionIndex = 0;
                            foreach (var guideQuestion in guideQuestions)
                            {                             
                                UpdateID(guideQuestion, "guidequestion", dt);
                                questionIndex++;
                            }
                        }                       
                    }                   
                    // Update Follow-up Questions
                    if (section["followupsections"] != null)
                    {
                        foreach (var followSection in section["followupsections"])
                        {                                                     
                            UpdateID(followSection, "question", dt);                            
                        }
                    }
                    //foreach (var followSection in section["followupsections"])
                    //{
                    //    JToken childFollowSection = null;
                    //    JArray? followSectionArray = null;
                    //    if (sectionsArray != null)
                    //    {
                    //        followSectionArray = sectionsArray[sectionIndex]?["followupsections"] as JArray;
                    //        if (followSectionArray != null)
                    //        {
                    //            childFollowSection = followSectionArray[childIndex];
                    //        }
                    //    }
                        
                    //    if (followSection?["question"] is JArray followQuestions && followQuestions.Count > 0)
                    //    {
                    //        JArray? followQuestionArray = null;
                    //        if(childFollowSection != null)
                    //        {
                    //            followQuestionArray = childFollowSection["question"] as JArray;
                    //        }
                    //        Int16 questionIndex = 0;
                    //        foreach (var followQuestion in followQuestions)
                    //        {
                    //            JToken questioToken = null;
                    //            if (followQuestionArray.Count >= (questionIndex + 1))
                    //            {
                    //                questioToken = followQuestionArray[questionIndex];
                    //            }
                    //            UpdateID(followQuestion, "followupquestion", dt, questioToken);
                    //            questionIndex++;
                    //        }
                    //    }
                    //    childIndex++;
                    //}
                    sectionIndex++;
                }
            }
            catch(Exception ex)
            {
                //return json;
            }

            return json;
        }

        static void UpdateID(JToken question, string fieldName, DataTable dt)
        {
            if (question.SelectToken(fieldName) != null)
            {                
                if(dt != null)
                {
                    string questionText = question[fieldName].ToString();
                    DataRow[] foundRows = dt.Select("QuestionDescription = '" + questionText.Replace("'", "''") + "'");

                    if (foundRows.Length > 0)
                    {
                        question["id"] = foundRows[0]["QuestionID"].ToString();
                    }
                }
            }
        }

        static string SanitizeXml(string xml)
        {
            StringBuilder result = new StringBuilder();
            bool insideTag = false;
            StringBuilder tagContent = new StringBuilder();

            foreach (char c in xml)
            {
                if (c == '<')
                {
                    // Append previously processed content as encoded text
                    if (tagContent.Length > 0)
                    {
                        result.Append(SecurityElement.Escape(tagContent.ToString())); // Encode inner text
                        tagContent.Clear();
                    }
                    insideTag = true;
                    result.Append(c);
                }
                else if (c == '>')
                {
                    insideTag = false;
                    result.Append(c);
                }
                else if (insideTag)
                {
                    result.Append(c);
                }
                else
                {
                    if (IsValidXmlChar(c))
                    {
                        tagContent.Append(c);
                    }
                }
            }

            // Append remaining text outside of last tag
            if (tagContent.Length > 0)
            {
                result.Append(SecurityElement.Escape(tagContent.ToString())); // Encode last content
            }

            return result.ToString();
        }

        static bool IsValidXmlChar(char c)
        {
            return (c == 0x9 || c == 0xA || c == 0xD ||  // Allowed control characters
                   (c >= 0x20 && c <= 0xD7FF) ||       // Basic Multilingual Plane
                   (c >= 0xE000 && c <= 0xFFFD) ||     // Special Unicode Range
                   (c >= 0x10000 && c <= 0x10FFFF));   // Supplementary Planes
        }

        static string ConvertXmlToJson(XDocument xmlDoc)
        {
            var root = xmlDoc.Root;
            if (root == null) return "{}";

            var jsonData = new Dictionary<string, object>();

            // Get total sections
            XElement totalSectionsElement = root.Element("totalsections");
            jsonData["totalsections"] = totalSectionsElement != null ? (object)totalSectionsElement.Value : null;

            // Get sections
            var sections = new List<Dictionary<string, object>>();
            Int16 sectionNumber = 1;
            List<Dictionary<string, object>> lstMainQuestions = new List<Dictionary<string, object>>();
            List<Dictionary<string, object>> lstAllSections = new List<Dictionary<string, object>>();
            //var allSections = new Dictionary<string, object>();
            if (root.Elements("allsections") != null && root.Elements("allsections").Count() > 0)
            {
                foreach (XElement section in root.Elements("allsections").Elements("section"))
                {
                    var sectionData = new Dictionary<string, object>();
                    XElement sectionNameElement = section.Element("sectionname");
                    XElement sectionFullNameElement = section.Element("sectionfullname");
                    sectionData["name"] = HttpUtility.HtmlDecode(sectionNameElement?.Value ?? "Unknown Section");
                    sectionData["fullname"] = HttpUtility.HtmlDecode(sectionFullNameElement?.Value ?? "Unknown Section");
                    //var questionData = new Dictionary<string, object>
                    //{
                    //    { "section", sectionData }                        
                    //};
                    lstAllSections.Add(sectionData);
                }
            }
            //allSections["allsections"] = lstAllSections;
             if (root.Elements("sections") != null && root.Elements("sections").Count() > 0)
            {
                if (root.Elements("endmessage") != null)
                {
                    XElement endmessageElement = root.Elements("endmessage")
                                      .FirstOrDefault();
                    if (endmessageElement != null)
                    {
                        jsonData["endmessage"] = endmessageElement != null ? (object)endmessageElement.Value : null;
                    }
                }
                else if (root.Elements("sections").Elements("endmessage") != null)
                {
                   XElement endmessageElement = root.Elements("sections")
                                     .Elements("endmessage")
                                     .FirstOrDefault();
                    if (endmessageElement != null)
                    {
                        jsonData["endmessage"] = endmessageElement != null ? (object)endmessageElement.Value : null;
                    }
                }
               
                
                
                foreach (XElement section in root.Elements("sections").Elements("section"))
                {
                    lstMainQuestions = new List<Dictionary<string, object>>();
                    var sectionData = new Dictionary<string, object>();
                    var waitData = new Dictionary<string, object>();
                    XElement sectionNameElement = section.Element("sectionname");
                    XElement sectionFullNameElement = section.Element("sectionfullname");
                    XElement waitElement = section.Element("wait");
                    sectionData["name"] = HttpUtility.HtmlDecode(sectionNameElement != null ? sectionNameElement.Value : "Unknown Section");
                    sectionData["fullname"] = HttpUtility.HtmlDecode(sectionFullNameElement != null ? sectionFullNameElement.Value : "Unknown Section");
                    sectionData["sectionnum"] = sectionNumber;
                    sectionData["iscomplete"] = "0";

                    // Extract mainsection
                    XElement mainSectionElement = section.Element("mainsection");
                    var mainSectionData = new Dictionary<string, object>();
                    if (mainSectionElement != null)
                    {
                        foreach (XElement mainNodeQuestions in mainSectionElement.Descendants("mainquestions"))
                        {
                            foreach (XElement mainNodeQuestion in mainNodeQuestions.Descendants("question"))
                            {
                                var mainQuestions = new List<Dictionary<string, object>>();
                                var guideQuestions = new List<Dictionary<string, object>>();

                                XElement mainElement = mainNodeQuestion.Element("main");
                                //if (mainElement != null)
                                //{
                                //    mainSectionData["description"] = mainElement.Value;
                                //    //mainSectionData["wait"] = waitElement != null ? waitElement.Value : "Please provide an assessment based on the question and guiding prompts.";
                                //}
                                XElement guideElement = mainNodeQuestion.Element("guide");                                
                                XElement mainQuestion = mainNodeQuestion.Element("mainquestion");
                                XElement mainQuestionAnswer = mainNodeQuestion.Element("answer");
                                XElement mainQuestionWait = mainNodeQuestion.Element("wait");

                                if (mainNodeQuestion.Elements("guidequestions") != null)
                                {
                                    foreach (XElement guideQuestionElement in mainNodeQuestion.Elements("guidequestions").Elements("guidequestion"))
                                    {
                                        guideQuestions.Add(new Dictionary<string, object> { { "guidequestion", HttpUtility.HtmlDecode(guideQuestionElement.Value) }, { "id", "0" } });
                                    }
                                }
                                //if (guideElement != null)
                                //{
                                //    mainSectionData["guide"] = new { description = guideElement.Value, guidequestions = guideQuestions };
                                //}
                                var questionData = new Dictionary<string, object>
                                {
                                    { "description", HttpUtility.HtmlDecode(mainElement?.Value ?? "") },
                                    { "mainquestion", HttpUtility.HtmlDecode(mainQuestion?.Value ?? "") }
                                    //,{"answer", HttpUtility.HtmlDecode(mainQuestionAnswer?.Value ?? "")}
                                    ,{"answer", "" }
                                    ,{"id", "0" }
                                    ,{"wait", HttpUtility.HtmlDecode(mainQuestionWait?.Value ?? "Please provide an assessment based on the question and guiding prompts.")}
                                    ,{"guide", new { description = HttpUtility.HtmlDecode(guideElement?.Value??""), guidequestions = guideQuestions }}
                                };
                                lstMainQuestions.Add(questionData);
                            }
                        }      
                  
                        sectionData["mainsection"] = lstMainQuestions;
                    }

                    // Extract follow-up sections dynamically
                    var followUpSections = new Dictionary<string, object>();
                    var questions = new List<Dictionary<string, object>>();
                    List<Dictionary<string, object>> lstfollowUps = new List<Dictionary<string, object>>();
                    var otherSections = new Dictionary<string, object>();

                    foreach (XElement childElement in section.Elements())
                    {
                        if (childElement.Name.LocalName.StartsWith("followupsection"))
                        {
                            followUpSections = new Dictionary<string, object>();
                            if (childElement.Name.LocalName == "followupsection")
                            {
                                XElement followupElement = childElement.Element("followup");
                                //followUpSections["description"] = HttpUtility.HtmlDecode((followupElement != null) ? followupElement.Value : "Follow-up Question:");
                               
                                foreach (XElement question in childElement.Elements("question"))
                                {
                                    XElement followupQuestion = question.Element("followupquestion");
                                    XElement answer = question.Element("answer");
                                    XElement followupWait = question.Element("wait");

                                    var questionData = new Dictionary<string, object>
                                    {
                                        { "description", HttpUtility.HtmlDecode((followupElement != null) ? followupElement.Value : "Follow-up Question:") },
                                        { "question", HttpUtility.HtmlDecode(followupQuestion != null ? followupQuestion.Value : "") },
                                        { "answer", HttpUtility.HtmlDecode(answer != null ? answer.Value : "") },{"id", "0" },
                                        { "wait", HttpUtility.HtmlDecode(followupWait?.Value ?? "Please provide additional details.") }
                                    };
                                    questions.Add(questionData);
                                }
                                //followUpSections["question"] = questions;
                                //lstfollowUps.Add(followUpSections);
                            }
                        }
                        else if (childElement.Name.LocalName == "othersection")
                        {
                            followUpSections = new Dictionary<string, object>();
                            //var otherSection = new Dictionary<string, object>
                            //{
                            //    { "description", "Other Questions" }
                            //};
                            //var otherQuestions = new List<Dictionary<string, object>>();
                            //followUpSections["description"] = "Other Questions:";
                            foreach (XElement question in childElement.Elements("question"))
                            {
                                XElement otherQuestion = question.Element("otherquestion");
                                XElement answer = question.Element("answer");
                                XElement otherWait = question.Element("wait");

                                var questionData = new Dictionary<string, object>
                                {
                                    { "description", "Other Questions:" },
                                    { "followupquestion", HttpUtility.HtmlDecode(otherQuestion != null ? otherQuestion.Value : "") },
                                    { "answer", HttpUtility.HtmlDecode(answer != null ? answer.Value : "") },{"id", "0" },
                                    { "wait", HttpUtility.HtmlDecode(otherWait?.Value ?? "Please provide additional details.") }
                                };
                                questions.Add(questionData);
                            }
                            //followUpSections["question"] = otherQuestions;
                            //lstfollowUps.Add(followUpSections);
                            //sectionData.Add("followupsection", followUpSections);
                            //otherSections["othersection"] = otherSection;
                        }
                    }
                    sectionData["followupsections"] = questions;
                    //if (lstfollowUps.Count > 0)
                    //    sectionData["followupsections"] = lstfollowUps;

                    //if (otherSections.Count > 0)
                    //    sectionData["othersection"] = otherSections;

                    sections.Add(sectionData);
                    sectionNumber++;
                }
            }            
            jsonData["sections"] = sections;
            jsonData["allsections"] = lstAllSections;
            return JsonConvert.SerializeObject(jsonData, Newtonsoft.Json.Formatting.Indented);
        }

        public static DataSet ConvertJsonToDataSet(string json)
        {
            DataSet dataSet = new DataSet("JsonDataSet");
            JObject jsonObj = JObject.Parse(json);
            Dictionary<string, DataTable> tableLookup = new Dictionary<string, DataTable>();

            foreach (JProperty property in jsonObj.Properties())
            {
                AddToDataSet(dataSet, tableLookup, property.Name, property.Value, null);
            }

            return dataSet;
        }

        public static DataSet ConvertJsonToDataSet1(string json)
        {
            DataSet dataSet = new DataSet();
            JObject jsonObject = JObject.Parse(json);

            foreach (var token in jsonObject)
            {
                string tableName = token.Key;
                JToken value = token.Value;

                if (value is JArray array)
                {
                    DataTable table = new DataTable(tableName);
                    foreach (var item in array)
                    {
                        if (item is JObject obj)
                        {
                            AddJsonToDataTable1(obj, table, dataSet, tableName);
                        }
                    }
                    dataSet.Tables.Add(table);
                }
                else
                {
                    DataTable table = new DataTable("Root");
                    table.Columns.Add(tableName, typeof(string));
                    table.Rows.Add(value.ToString());
                    dataSet.Tables.Add(table);
                }
            }

            return dataSet;
        }

        private static void AddJsonToDataTable1(JObject jsonObject, DataTable table, DataSet dataSet, string parentTableName)
        {
            DataRow row = table.NewRow();

            foreach (var prop in jsonObject.Properties())
            {
                if (prop.Value is JObject nestedObject)
                {
                    // Create a new table for nested objects
                    string nestedTableName = $"{parentTableName}_{prop.Name}";
                    DataTable nestedTable = new DataTable(nestedTableName);
                    AddJsonToDataTable1(nestedObject, nestedTable, dataSet, nestedTableName);
                    dataSet.Tables.Add(nestedTable);
                }
                else if (prop.Value is JArray nestedArray)
                {
                    // Create a new table for nested arrays
                    string nestedTableName = $"{parentTableName}_{prop.Name}";
                    DataTable nestedTable = new DataTable(nestedTableName);
                    foreach (var item in nestedArray)
                    {
                        if (item is JObject obj)
                        {
                            AddJsonToDataTable1(obj, nestedTable, dataSet, nestedTableName);
                        }
                    }
                    dataSet.Tables.Add(nestedTable);
                }
                else
                {
                    if (!table.Columns.Contains(prop.Name))
                        table.Columns.Add(prop.Name, typeof(string));

                    row[prop.Name] = prop.Value.ToString();
                }
            }
            table.Rows.Add(row);
        }

        public static string ConvertJsonToFormattedText(string json)
        {
            StringBuilder sb = new StringBuilder();
            try
            {                
                JObject jsonObject = JObject.Parse(json);
                
                int totalSections = 1;
                if (jsonObject["totalsections"] != null && jsonObject["totalsections"].ToString().Length > 0)
                {
                    totalSections = int.Parse(jsonObject["totalsections"].ToString());
                }
                string includedSteps = "Include Step 1 of " + totalSections.ToString();
                if (jsonObject["sections"] is JArray sections)
                {
                    Int16 sectionNum = 1;
                    foreach (var section in sections)
                    {
                        bool isCompletedSection = true;
                        sb.AppendLine($"{section["fullname"]} \n");
                        

                        if (section["mainsection"] != null)
                        {
                            foreach (var mainSection in section["mainsection"])
                            {
                                if (mainSection["description"] != null)
                                {
                                    sb.AppendLine(mainSection["description"].ToString() + "\n");
                                }

                                if (mainSection?["mainquestion"] != null)
                                {
                                    sb.AppendLine(mainSection["mainquestion"].ToString() + "\n");
                                }

                                if (mainSection?["guide"]?["description"] != null)
                                {
                                    sb.AppendLine(mainSection["guide"]["description"].ToString() + "\n");
                                }

                                if (mainSection?["guide"]?["guidequestions"] is JArray guideQuestions && guideQuestions.Count > 0)
                                {
                                    foreach (var guideQuestion in guideQuestions)
                                    {
                                        sb.AppendLine("• " + guideQuestion["guidequestion"]?.ToString() + "\n");
                                    }
                                }
                                if (mainSection?["wait"] != null)
                                {
                                    sb.AppendLine(mainSection["wait"].ToString() + "\n");
                                }
                                if (mainSection?["answer"] != null && mainSection["answer"].ToString().Length > 0)
                                {
                                    sb.AppendLine("Answer: " + mainSection["answer"].ToString() + "\n");
                                }
                                else
                                {
                                    isCompletedSection = false;
                                }
                            }
                        }


                        if (section["followupsections"] is JArray followupSections && followupSections.Count > 0)
                        {
                            foreach (var followup in followupSections)
                            {
                                if (followup["question"] is JArray followupQuestions1 && followupQuestions1.Count > 0)
                                {
                                    if (followup["description"] != null)
                                    {
                                        sb.AppendLine(followup["description"].ToString() + "\n");
                                    }
                                }

                                if (followup["question"] is JArray followupQuestions && followupQuestions.Count > 0)
                                {
                                    foreach (var question in followupQuestions)
                                    {
                                        if (question["followupquestion"]?.ToString().Length > 0)
                                        {
                                            sb.AppendLine("• " + question["followupquestion"]?.ToString() + "\n");
                                            if (question?["wait"] != null)
                                            {
                                                sb.AppendLine(question["wait"]?.ToString() + "\n");
                                            }
                                            if (question?["answer"] != null && question["answer"]?.ToString().Length > 0)
                                            {
                                                sb.AppendLine("Answer: " + question["answer"]?.ToString() + "\n");
                                            }
                                            else
                                            {
                                                isCompletedSection = false;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        sectionNum++;
                        if (isCompletedSection)
                        {
                            if (sectionNum <= totalSections)
                            {
                                includedSteps += $" and Step {sectionNum} of {totalSections.ToString()}";
                            }
                            if(sectionNum == totalSections)
                            {
                                includedSteps += $" and <endmessage></endmessage>";
                            }
                        }
                        
                        //sb.AppendLine("\n"); // Add spacing between sections
                    }
                    sb.AppendLine(includedSteps + "\n");
                }
            }
            catch(Exception ex)
            {

            }

            return sb.ToString();
        }

        public static void ConvertColumnsToString(DataSet ds)
        {
            foreach (DataTable dt in ds.Tables)
            {
                DataColumn[] newColumns = new DataColumn[dt.Columns.Count];

                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    DataColumn oldColumn = dt.Columns[i];

                    // Create a new string column with the same name
                    newColumns[i] = new DataColumn(oldColumn.ColumnName + "_new", typeof(string));
                }

                // Add new string columns
                foreach (DataColumn newColumn in newColumns)
                {
                    dt.Columns.Add(newColumn);
                }

                // Copy data from old columns to new string columns
                foreach (DataRow row in dt.Rows)
                {
                    for (int i = 0; i < dt.Columns.Count / 2; i++)
                    {
                        row[newColumns[i].ColumnName] = row[i].ToString();
                    }
                }

                // Remove old columns
                for (int i = dt.Columns.Count / 2 - 1; i >= 0; i--)
                {
                    dt.Columns.RemoveAt(i);
                }

                // Rename new columns to original names
                foreach (DataColumn col in dt.Columns)
                {
                    col.ColumnName = col.ColumnName.Replace("_new", "");
                }
            }
        }

        public static DataTable RemoveColumns(DataTable dtData, string[] columns)
        {
            var columnsToRemove = dtData.Columns
                                    .Cast<DataColumn>()
                                    .Where(col => !columns.Contains(col.ColumnName))
                                    .ToList();

            // Remove columns from DataTable
            foreach (var col in columnsToRemove)
            {
                dtData.Columns.Remove(col);
            }

            // add columns if not available
            foreach (string column in columns)
            {
                if (!dtData.Columns.Contains(column))
                {
                    dtData.Columns.Add(new DataColumn(column, typeof(string)));
                }
            }

            return dtData;
        }

        static void AddToDataSet(DataSet dataSet, Dictionary<string, DataTable> tableLookup, string tableName, JToken token, DataRow parentRow)
        {
            if (token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                DataTable table = GetOrCreateTable(dataSet, tableLookup, tableName, obj);
                DataRow row = table.NewRow();
                row["ID"] = idCounter++; // Assign a unique incremental integer ID

                if (parentRow != null)
                {
                    row["ParentID"] = parentRow["ID"]; // Set ParentID for relationships
                }

                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.Array || property.Value.Type == JTokenType.Object)
                    {
                        AddToDataSet(dataSet, tableLookup, property.Name, property.Value, row);
                    }
                    else
                    {
                        row[property.Name] = property.Value.ToString();
                    }
                }

                table.Rows.Add(row);
            }
            else if (token.Type == JTokenType.Array)
            {
                if (((JArray)token).Count() > 0)
                {
                    foreach (JToken item in (JArray)token)
                    {
                        AddToDataSet(dataSet, tableLookup, tableName, item, parentRow);
                    }
                }
                else
                {
                    DataTable table = GetOrCreateTable(dataSet, tableLookup, tableName, null);

                }
            }
            else if (token.Type == JTokenType.String || token.Type == JTokenType.Integer)
            {
                DataTable table = GetOrCreateTable(dataSet, tableLookup, tableName);
                DataRow row = table.NewRow();
                row["ID"] = idCounter++;
                row["Value"] = token.ToString();

                if (parentRow != null)
                {
                    row["ParentID"] = parentRow["ID"];
                }

                table.Rows.Add(row);
            }
        }

        static DataTable GetOrCreateTable(DataSet dataSet, Dictionary<string, DataTable> tableLookup, string tableName, JObject schema = null)
        {
            if (!tableLookup.ContainsKey(tableName))
            {
                DataTable table = new DataTable(tableName);

                table.Columns.Add("ID", typeof(int)); // Integer ID instead of GUID
                table.Columns.Add("ParentID", typeof(int)); // Integer Parent ID for relations

                if (schema != null)
                {
                    foreach (JProperty property in schema.Properties())
                    {
                        table.Columns.Add(property.Name, typeof(string));
                    }
                }
                else
                {
                    table.Columns.Add("Value", typeof(string));
                }

                dataSet.Tables.Add(table);
                tableLookup[tableName] = table;
            }

            return tableLookup[tableName];
        }
    }
}
