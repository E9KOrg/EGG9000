using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class AutomationLog {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid id { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public string Type { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public bool Skipped { get; set; }

        public AutomationLog() {
        }

    }
}
