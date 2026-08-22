using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    public class EventCustomization {
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
        private readonly JsonBlobAccessor<EventCustomizationSettings> _settingsAccessor = new("{}");
        [NotMapped]
        public EventCustomizationSettings Settings {
            get => _settingsAccessor.Get(_settings);
            set => _settings = _settingsAccessor.Set(value, _settings);
        }
    }

    public class EventCustomizationSettings {
        public List<EventNotification> Notifications { get; set; }
    }

    public class EventNotification {
        public ulong GuildID { get; set; }
        public decimal MinValue { get; set; }
        public ulong RoleID { get; set; }
        public string RoleIdString {
            get {
                return RoleID.ToString();
            }
            set {
                RoleID = ulong.Parse(value);
            }
        }
    }
}
