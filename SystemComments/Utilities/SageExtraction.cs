using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.VisualStudio.Web.CodeGeneration.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using SystemComments.Models.DataBase;
using static System.Collections.Specialized.BitVector32;

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
            airesponse = airesponse.Replace("```", "");
            airesponse = Regex.Replace(airesponse, @"^[^<]*|[^>]*$", "", RegexOptions.Singleline);
            airesponse = Regex.Replace(airesponse, @"<(\w+)>([^<]+)<\1>", "<$1>$2</$1>");
            airesponse = SanitizeXml(airesponse);
            string wrappedXml = "<root>" + airesponse + "</root>";
            XDocument xmlDoc = XDocument.Parse(wrappedXml);
            return ConvertXmlToJson(xmlDoc);
        }

        public static Int32 GetSectionsCount(string aiJSON)
        {
            Int32 count = 0;
            try
            {
                JObject jsonObject = JObject.Parse(aiJSON);
                JArray? sectionsArray = jsonObject["sections"] as JArray;
                if(sectionsArray != null)
                {
                    count = sectionsArray.Count;
                }
            }
            catch(Exception ex)
            {
                count = 0;
            }
            return count;
        }

        public static Int32 GetAllSectionsCount(string aiJSON)
        {
            Int32 count = 0;
            try
            {
                JObject jsonObject = JObject.Parse(aiJSON);
                JArray? sectionsArray = jsonObject["allsections"] as JArray;
                if (sectionsArray != null)
                {
                    count = sectionsArray.Count;
                }
            }
            catch (Exception ex)
            {
                count = 0;
            }
            return count;
        }

        public static string InsertSection(string aiJSON, string defaultJSON, Int32 sectionNum)
        {
            try
            {
                string updatedJSON = "";
                if (aiJSON.Length > 2 && defaultJSON.Length > 2)
                {
                    JObject jsonObject = JObject.Parse(aiJSON);
                    JObject objDefaultJSON = JObject.Parse(defaultJSON);
                    UpdateProperties(objDefaultJSON);
                    JArray sectionsArray = (JArray)jsonObject["sections"];
                    JArray defaultSectionsArray = (JArray)objDefaultJSON["sections"];
                    if (defaultSectionsArray != null && defaultSectionsArray.Count >= sectionNum)
                    {
                        JObject selectedSection = (JObject)defaultSectionsArray[sectionNum];
                        if (selectedSection != null && (sectionsArray.Count < sectionNum + 1))
                        {
                            sectionsArray.Add(selectedSection);
                        }
                        if (jsonObject["totalsections"] != null && objDefaultJSON["totalsections"] != null)
                        {
                            jsonObject["totalsections"] = objDefaultJSON["totalsections"];
                        }
                        if (jsonObject["allsections"] != null && objDefaultJSON["allsections"] != null)
                        {
                            JArray allSectionsArray = (JArray)jsonObject["allsections"];
                            if(allSectionsArray.Count == 0)
                            {
                                allSectionsArray = (JArray)objDefaultJSON["allsections"];
                                jsonObject["allsections"] = allSectionsArray;
                            }
                            
                        }
                        updatedJSON = JsonConvert.SerializeObject(jsonObject, Formatting.None);
                    }
                }

                return updatedJSON;
            }
            catch(Exception ex)
            {
                return aiJSON;
            }
        }

        private static void UpdateProperties(JToken token)
        {
            if (token is JProperty property)
            {
                if (property.Name == "id")
                {
                    property.Value = "0";
                }
                else if (property.Name == "answer")
                {
                    property.Value = string.Empty;
                }
            }

            // Recursively iterate through all children
            foreach (var child in token.Children())
            {
                UpdateProperties(child);
            }
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
                            if (section["mainsection"] != null)
                            {
                                foreach (var mainSection in section["mainsection"])
                                {
                                    JArray mainSectionArray = tempSectionArray?["mainsection"] as JArray;
                                    if (mainSectionArray == null)
                                    {
                                        var mainSectionToken = new JArray();
                                        mainSectionToken.Add(mainSection);
                                        tempSectionArray["mainsection"] = mainSectionToken;
                                    }
                                    else if (mainSectionArray.Count == 0)
                                    {
                                        var mainSectionToken = new JArray();
                                        mainSectionToken.Add(mainSection);
                                        tempSectionArray["mainsection"] = mainSectionToken;
                                    }
                                    mainSectionIndex++;
                                }
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
                                            else if(followSectionArray != null && followSectionArray.Count == 0)
                                            {
                                                newItems.Add(followSection);
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
                    Int32 mainIndex = 1;
                    if (section["mainsection"] != null)
                    {
                        foreach (var mainSection in section["mainsection"])
                        {
                            UpdateID(mainSection, "mainquestion", dt, 3, mainIndex, (sectionIndex + 1));

                            if (mainSection?["guide"]?["guidequestions"] is JArray guideQuestions && guideQuestions.Count > 0)
                            {
                                Int32 questionIndex = 0;
                                foreach (var guideQuestion in guideQuestions)
                                {
                                    UpdateID(guideQuestion, "guidequestion", dt, 2, questionIndex + 1, (sectionIndex + 1));
                                    questionIndex++;
                                }
                            }
                            mainIndex++;
                        }
                    }
                    // Update Follow-up Questions
                    if (section["followupsections"] != null)
                    {
                        foreach (var followSection in section["followupsections"])
                        {
                            Int32 followupIndex = 1;
                            UpdateID(followSection, "question", dt, 1, followupIndex, (sectionIndex + 1));
                            followupIndex++;
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

        static void UpdateID(JToken question, string fieldName, DataTable dt, Int16 headerType, Int32 questionIndex, Int32 sectionNum)
        {
            //headerType 1 => followup; 2 => guide; 3 => main; 4 => wait
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
                    else if (question["id"] != null && question["id"].ToString() == "0")
                    {
                        foundRows = dt.Select("HeaderTypeID = " + headerType + " AND QuestionIndex=" + questionIndex + " AND SectionNumber=" + sectionNum);
                        if (foundRows.Length > 0)
                        {
                            question["id"] = foundRows[0]["QuestionID"].ToString();
                        }
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
                                    if (followupQuestion != null && followupQuestion.Value.Length > 0)
                                    {
                                        var questionData = new Dictionary<string, object>
                                    {
                                        { "description", HttpUtility.HtmlDecode((followupElement != null) ? followupElement.Value : "Follow-up Question:") },
                                        { "question", HttpUtility.HtmlDecode(followupQuestion != null ? followupQuestion.Value : "") },
                                        { "answer", HttpUtility.HtmlDecode(answer != null ? answer.Value : "") },{"id", "0" },
                                        { "wait", HttpUtility.HtmlDecode(followupWait?.Value ?? "Please provide additional details.") }
                                    };
                                        questions.Add(questionData);
                                    }
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
            sections = sections
           .GroupBy(s => s["name"]?.ToString())
           .Select(g => g.First())
           .ToList();

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

        public static string ChangeJSONOrder(string json)
        {
            try
            {
                // Deserialize the JSON to a JObject
                JObject jsonObject = JObject.Parse(json);

                // Get the "endmessage" value                

                JToken allSections = jsonObject["allsections"];
                if (allSections != null)
                {
                    // Remove "endmessage" from its current position
                    jsonObject.Remove("allsections");
                    jsonObject["allsections"] = allSections;
                }
                JToken endMessage = jsonObject["endmessage"];
                if (endMessage != null)
                {
                    // Remove "endmessage" from its current position
                    jsonObject.Remove("endmessage");
                    jsonObject["endmessage"] = endMessage;
                }

                // Serialize the modified object back to JSON
                string modifiedJson = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                return modifiedJson;
            }
            catch(Exception ex)
            {
                return json;
            }
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

        public static string ConvertLastJsonToFormattedText(string json, ref Int32 lastSection, ref Int32 noOfSections)
        {
            StringBuilder sb = new StringBuilder();
            string includedSteps = "IMPORTANT: For this response, generate exactly the following sections::\n\t\t";
            try
            {
                JObject jsonObject = JObject.Parse(json);
                Int32 totalSections = 1;
                Int32 currentSection = 0;
                //Int32 followupSection = 1;
                if (jsonObject["totalsections"] != null && jsonObject["totalsections"].ToString().Length > 0)
                {
                    totalSections = int.Parse(jsonObject["totalsections"].ToString());
                }

                var sections = jsonObject["sections"];
                // Find last section with non-empty mainsection.answer
                var objLastSection = sections
                    .Where(s =>
                        s["mainsection"] != null &&
                        s["mainsection"].Any(ms => !string.IsNullOrWhiteSpace((string)ms["answer"])))
                    .LastOrDefault();

                if (objLastSection != null)
                {
                    currentSection = int.Parse(objLastSection["sectionnum"].ToString());
                    //sb.Append("The sectionnum field is derived from the section name (fullname) in the JSON. For example, if the section name is ‘Section 2 of 5’, then sectionnum is 2. \n");
                    sb.Append($"Total Sections: {totalSections} \n");
                    Int16 startStep = 1;
                    while (startStep < currentSection)
                    {
                        sb.Append($"Section {startStep} completed.\n");
                        startStep++;
                    }
                    if (objLastSection["mainsection"] != null)
                    {
                        foreach (var mainSection in objLastSection["mainsection"])
                        {
                            if (mainSection?["mainquestion"] != null && mainSection["mainquestion"].ToString().Length > 0)
                            {
                                //if (mainSection["description"] != null)
                                //{
                                //    sb.Append(mainSection["description"].ToString() + "\n");
                                //}

                                //if (mainSection?["mainquestion"] != null)
                                //{
                                //    sb.Append(mainSection["mainquestion"].ToString() + "\n");
                                //}

                                //if (mainSection?["guide"]?["description"] != null)
                                //{
                                //    sb.Append(mainSection["guide"]["description"].ToString() + "\n");
                                //}

                                //if (mainSection?["guide"]?["guidequestions"] is JArray guideQuestions && guideQuestions.Count > 0)
                                //{
                                //    foreach (var guideQuestion in guideQuestions)
                                //    {
                                //        sb.Append("• " + guideQuestion["guidequestion"]?.ToString() + "\n");
                                //    }
                                //}
                                //if (mainSection?["wait"] != null)
                                //{
                                //    sb.Append(mainSection["wait"].ToString() + "\n");
                                //}
                                if (mainSection?["answer"] != null && mainSection["answer"].ToString().Length > 0)
                                {
                                    //sb.Append($"Section {currentSection}:\nMain Question Answer: " + ((mainSection["answer"].ToString().Length > 50) ? mainSection["answer"].ToString().Substring(0, 50) + "..." : mainSection["answer"].ToString())  + "\n");
                                    sb.Append($"Section {currentSection}:\nMain Question Answer: " + mainSection["answer"].ToString() + "\n");                                  
                                }
                            }                           
                        }
                    }
                    if (objLastSection["followupsections"] != null && objLastSection["followupsections"] is JArray followupSections && followupSections.Count > 0)
                    {
                        foreach (var followup in followupSections)
                        {
                            if (followup["question"]?.ToString().Length > 0)
                            {
                            //    if (followup["description"] != null)
                            //    {
                            //        sb.Append(followup["description"].ToString() + "\n");
                            //    }

                            //    sb.Append("• " + followup["question"]?.ToString() + "\n");
                            //    if (followup["wait"] != null)
                            //    {
                            //        sb.Append(followup["wait"]?.ToString() + "\n");
                            //    }
                                if (followup["answer"] != null && followup["answer"]?.ToString().Length > 0)
                                {
                                    //sb.Append("Answer: User Completed this question.\n");
                                    sb.Append("Followup Question Answer: " + followup["answer"]?.ToString() + "\n");
                                    //sb.Append("Followup Question Answer: " + ((followup["answer"].ToString().Length > 50) ? followup["answer"].ToString().Substring(0, 50) + "..." : followup["answer"].ToString()) + "\n");
                                }                               
                            }                            

                        }
                    }
                }

                // Find last section with at least one non-empty followupsections.answer
                //var lastFollowupSection = sections
                //    .Where(s =>
                //        s["followupsections"] != null &&
                //        s["followupsections"].Any(fs => !string.IsNullOrWhiteSpace((string)fs["answer"])))
                //    .LastOrDefault();
                //if (lastFollowupSection != null)
                //{
                //    followupSection = int.Parse(lastFollowupSection["sectionnum"].ToString());
                //}


                //string initialStep = $"1. Section {((currentSection == 0) ? 1 : currentSection)} of {totalSections.ToString()}";
                //includedSteps += $"{initialStep}\n\t\t";
                if (currentSection > 0 && currentSection < totalSections)
                {
                    //int previousSection = currentSection;
                    currentSection++;
                    //includedSteps += $"2. Section {currentSection} of {totalSections.ToString()}.";
                }


                lastSection = ((currentSection == 0) ? 1 : currentSection);
                noOfSections = totalSections;
                //sb.Append("\n\n" + includedSteps);

            }
            catch (Exception e)
            {

            }
            return sb.ToString();
        }

        public static string ConvertJsonToFormattedText(string json, ref Int32 lastSection, ref Int32 noOfSections)
        {
            StringBuilder sb = new StringBuilder();
            try
            {                
                JObject jsonObject = JObject.Parse(json);
                
                Int32 totalSections = 1;
                if (jsonObject["totalsections"] != null && jsonObject["totalsections"].ToString().Length > 0)
                {
                    totalSections = int.Parse(jsonObject["totalsections"].ToString());
                }
                string includedSteps = "This is a structured request consisting of sections. Ensure that all sections are included.\n\n Include below steps with out fail\n\t\t 1. Section 1 of " + totalSections.ToString();
                string followupInstructions = "";
                sb.Append($"Total Sections: {totalSections} \n");
                if (jsonObject["sections"] is JArray sections)
                {
                    Int16 sectionNum = 1;
                    bool isCompleteOneAnswer = false;
                    foreach (var section in sections)
                    {
                        isCompleteOneAnswer = false;
                        bool isCompletedSection = true;
                        sb.Append($"{section["fullname"]} \n");
                        

                        if (section["mainsection"] != null)
                        {
                            foreach (var mainSection in section["mainsection"])
                            {
                                if (mainSection?["mainquestion"] != null && mainSection["mainquestion"].ToString().Length > 0)
                                {
                                    if (mainSection["description"] != null)
                                    {
                                        sb.Append(mainSection["description"].ToString() + "\n");
                                    }

                                    if (mainSection?["mainquestion"] != null)
                                    {
                                        sb.Append(mainSection["mainquestion"].ToString() + "\n");
                                    }

                                    if (mainSection?["guide"]?["description"] != null)
                                    {
                                        sb.Append(mainSection["guide"]["description"].ToString() + "\n");
                                    }

                                    if (mainSection?["guide"]?["guidequestions"] is JArray guideQuestions && guideQuestions.Count > 0)
                                    {
                                        foreach (var guideQuestion in guideQuestions)
                                        {
                                            sb.Append("• " + guideQuestion["guidequestion"]?.ToString() + "\n");
                                        }
                                    }
                                    if (mainSection?["wait"] != null)
                                    {
                                        sb.Append(mainSection["wait"].ToString() + "\n");
                                    }
                                    if (mainSection?["answer"] != null && mainSection["answer"].ToString().Length > 0)
                                    {
                                        sb.Append("Answer: " + mainSection["answer"].ToString() + "\n");
                                        //sb.Append("Answer: User Completed this question.\n");
                                        isCompleteOneAnswer = true;
                                    }
                                    else
                                    {
                                        isCompletedSection = false;
                                    }
                                }
                                else
                                {
                                    isCompletedSection = true;
                                }
                            }
                        }

                        if (section["followupsections"] is JArray followupSections && followupSections.Count > 0)
                        {
                            foreach (var followup in followupSections)
                            {                                
                                if (followup["question"]?.ToString().Length > 0)
                                {
                                    if (followup["description"] != null)
                                    {
                                        sb.Append(followup["description"].ToString() + "\n");
                                    }

                                    sb.Append("• " + followup["question"]?.ToString() + "\n");
                                    if (followup["wait"] != null)
                                    {
                                        sb.Append(followup["wait"]?.ToString() + "\n");
                                    }
                                    if (followup["answer"] != null && followup["answer"]?.ToString().Length > 0)
                                    {
                                        //sb.Append("Answer: User Completed this question.\n");
                                        sb.Append("Answer: " + followup["answer"]?.ToString() + "\n");
                                    }
                                    else
                                    {
                                        isCompletedSection = false;
                                    }
                                }
                                else
                                {
                                    isCompletedSection = true;
                                }

                            }
                        }
                        sectionNum++;
                        if (isCompletedSection)
                        {
                            if (sectionNum <= totalSections)
                            {
                                includedSteps += $"\n\t\t {sectionNum}. Section {sectionNum} of {totalSections.ToString()}";
                            }
                            //if(sectionNum >= 2 && sectionNum <= totalSections && isCompleteOneAnswer)
                            //{
                            //    followupInstructions += ((followupInstructions.Length == 0) ? $"Don't include the new followup questions for the sections Section {(sectionNum - 1)} of {totalSections.ToString()}" : $" and {(sectionNum - 1)} of {totalSections.ToString()}");
                            //}
                            //if(sectionNum == totalSections)
                            //{
                            //    includedSteps += $"\nInclude <endmessage></endmessage>";
                            //}
                        }
                        
                        sb.Append("\n"); // Add spacing between sections
                    }
                    lastSection = sectionNum;
                    noOfSections = totalSections;
                    sb.Append(followupInstructions + "\n\n" + includedSteps);
                    //sb.AppendLine(((followupInstructions.Length > 0) ? followupInstructions + "\n" : "") +  includedSteps + "\n Don't include next section if new followup questions are included into the current section. \n");
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
                        if (!table.Columns.Contains(property.Name))
                        {
                            table.Columns.Add(property.Name, typeof(string));
                        }
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
        public static string MergeJson(string json1, string json2)
        {
            try
            {
                //// Deserialize JSON 1
                //var json1Object = System.Text.Json.JsonSerializer.Deserialize<JsonRoot>(json1);

                //// Deserialize JSON 2
                //var json2Object = System.Text.Json.JsonSerializer.Deserialize<JsonRoot>(json2);

                //if (json1Object == null || json2Object == null)
                //    return json1; // If any JSON is invalid, return original JSON 1

                //// Convert sections to a dictionary for easy lookup
                //var sectionsMap = json1Object.Sections.ToDictionary(s => s.SectionNum, s => s);

                //foreach (var section in json2Object.Sections)
                //{
                //    if (sectionsMap.ContainsKey(section.SectionNum))
                //    {
                //        // Merge existing section
                //        var existingSection = sectionsMap[section.SectionNum];

                //        if (existingSection.MainSection == null || existingSection.MainSection.Count == 0)
                //        {
                //            existingSection.MainSection = section.MainSection;
                //        }

                //        if (existingSection.FollowupSections == null || existingSection.FollowupSections.Count == 0)
                //        {
                //            existingSection.FollowupSections = section.FollowupSections;
                //        }
                //    }
                //    else
                //    {
                //        // Add new section
                //        json1Object.Sections.Add(section);
                //    }
                //}

                //// Serialize back to JSON
                //return System.Text.Json.JsonSerializer.Serialize(json1Object, new JsonSerializerOptions { WriteIndented = true });

                JObject obj1 = JObject.Parse(json1);
                JObject obj2 = JObject.Parse(json2);

                var sections1 = (JArray)obj1["sections"];
                var sections2 = (JArray)obj2["sections"];
                int sectionsCount = sections1.Count;
                // Merge sections from JSON 2 if sectionnum not in JSON 1
                foreach (var section2 in sections2)
                {
                    int secNum2 = (int)section2["sectionnum"];
                    string sectionName = section2["name"].ToString();
                    bool exists = sections1.Any(s => (int)s["sectionnum"] == secNum2);
                    if (!exists)
                    {
                        sections1.Add(section2);
                    }
                    else
                    {                        

                        exists = sections1.Any(s => s["name"].ToString() == sectionName);
                        if(!exists)
                        {
                            sectionsCount++;
                            section2["sectionnum"] = sectionsCount.ToString();
                            sections1.Add(section2);
                        }
                        var existingSection = sections1.FirstOrDefault(s => s["name"].ToString() == sectionName) as JObject;
                        // Update Main Section if not available
                        if (existingSection["mainsection"] == null && section2["mainsection"] != null)
                        {                           
                            existingSection["mainsection"] = section2["mainsection"];
                        }

                        var followup1 = section2["followupsections"] as JArray; // AI JSON
                        var followup2 = existingSection["followupsections"] as JArray;

                        if ((followup2 == null || followup2.Count == 0) && followup1 != null)
                        {
                            existingSection["followupsections"] = followup1;
                        }
                        else 
                        {
                            if (followup2 != null && followup2.Count > 0)
                            {
                                foreach (var f2 in followup1)
                                {
                                    string f2Id = f2["id"]?.ToString();
                                    string f2Question = f2["question"]?.ToString();

                                    // check if JSON1 already has this follow-up by id or question
                                    exists = followup2.Any(f1 =>
                                        (f1["id"]?.ToString() == f2Id && !string.IsNullOrEmpty(f2Id)) ||
                                        f1["question"]?.ToString() == f2Question);

                                    if (!exists)
                                    {
                                        followup2.Add(f2.DeepClone()); // add missing follow-up
                                    }
                                }
                            }
                        }
                    }
                }

                // update totalsections just in case
                //obj1["totalsections"] = sections1.Count.ToString();

                return obj1.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                return json1;
            }
        }

        public static async Task<string> FormatHtml(string prompt)
        {
            string result = string.Empty;
            await Task.Run(() => {
                // Replace opening <ul><li> or <li> with newline + dash
                result = Regex.Replace(prompt, @"<\s*li\s*>", "\n- ");

                // Remove closing </li>
                result = Regex.Replace(result, @"<\s*/\s*li\s*>", "");

                // Remove <ul> and </ul>
                result = Regex.Replace(result, @"<\s*/?\s*ul\s*>", "");
            });

            return result;
        }

        public static string ExtractAndRemoveAllSections(ref string xml)
        {
            const string startTag = "<allsections>";
            const string endTag = "</allsections>";

            int start = xml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            int end = xml.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

            if (start >= 0 && end > start)
            {
                // Extract including the wrapper
                string block = xml.Substring(start, (end + endTag.Length) - start);

                // Remove it from the original string
                xml = xml.Remove(start, (end + endTag.Length) - start);

                return block; // return the extracted block
            }

            return string.Empty; // not found
        }
    }
    // JSON Model Classes
    public class JsonRoot
    {
        [JsonPropertyName("totalsections")]
        public string TotalSections { get; set; }

        [JsonPropertyName("sections")]
        public List<Section> Sections { get; set; }

        [JsonPropertyName("allsections")]
        public List<AllSection> AllSections { get; set; }
    }

    public class Section
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("fullname")]
        public string Fullname { get; set; }

        [JsonPropertyName("sectionnum")]
        public int SectionNum { get; set; }

        [JsonPropertyName("iscomplete")]
        public string IsComplete { get; set; }

        [JsonPropertyName("mainsection")]
        public List<MainSection> MainSection { get; set; }

        [JsonPropertyName("followupsections")]
        public List<FollowupSection> FollowupSections { get; set; }
    }

    public class MainSection
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("mainquestion")]
        public string MainQuestion { get; set; }

        [JsonPropertyName("answer")]
        public string Answer { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("wait")]
        public string Wait { get; set; }

        [JsonPropertyName("guide")]
        public Guide Guide { get; set; }
    }

    public class Guide
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("guidequestions")]
        public List<GuideQuestion> GuideQuestions { get; set; }
    }

    public class GuideQuestion
    {
        [JsonPropertyName("guidequestion")]
        public string GuideQuestionText { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    public class FollowupSection
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("question")]
        public string Question { get; set; }

        [JsonPropertyName("answer")]
        public string Answer { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("wait")]
        public string Wait { get; set; }
    }

    public class AllSection
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("fullname")]
        public string Fullname { get; set; }
    }
}
