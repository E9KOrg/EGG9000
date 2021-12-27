using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace EGG9000.Common.Database.Entities
{
    public class EventCustomization
    {
        [Key]
        public string Type { get; set; }

        public string Color { get; set; }
        public string Description { get; set; }
        public string Fields { get; set; }
        public string ThumbnailURL { get; set; }
        public string Emoji { get; set; }
        public int Priority { get; set; }

        public string _settings { get; set; }
        [NotMapped]
        public EventCustomizationSettings Settings
        {
            get
            {
                return JsonConvert.DeserializeObject<EventCustomizationSettings>(_settings ?? "{}");
            }

        }


    }

    public class EventField
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class EventCustomizationSettings
    {
        public List<EventNotification> Notifications { get; set; }
    }

    public class EventNotification
    {
        public ulong GuildID { get; set; }
        public decimal MinValue { get; set; }
        public ulong RoleID { get; set; }
    }
}
