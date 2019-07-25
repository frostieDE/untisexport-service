﻿using Newtonsoft.Json;
using System;

namespace UntisExportService.Core.Model
{
    public class LegacyIccSubstitution : ISubstitution
    {
        [JsonProperty("ID")]
        public int Id { get; set; }

        [JsonProperty("Date")]
        public DateTime Date { get; set; }

        [JsonProperty("Lesson")]
        public int Lesson { get; set; }

        [JsonProperty("AbsenceTeacher")]
        public string Teacher { get; set; }

        [JsonProperty("ReplacementTeacher")]
        public string ReplacementTeacher { get; set; }

        [JsonProperty("Subject")]
        public string Subject { get; set; }

        [JsonProperty("ReplacementSubject")]
        public string ReplacementSubject { get; set; }

        [JsonProperty("Room")]
        public string Room { get; set; }

        [JsonProperty("ReplacementRoom")]
        public string ReplacementRoom { get; set; }

        [JsonProperty("Classes")]
        public string[] Grades { get; set; }

        [JsonProperty("ReplacementClasses")]
        public string[] ReplacementGrades { get; set; }

        [JsonProperty("AbsenceReason")]
        public string Reason { get; set; }

        [JsonProperty("Description")]
        public string Description { get; set; }

        [JsonProperty("Type")]
        public string Type { get; set; }
    }
}
