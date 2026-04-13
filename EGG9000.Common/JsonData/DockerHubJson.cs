using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.JsonData {
    public class DockerHubJson {
        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
        public record PushData(
     int pushed_at,
     string pusher,
     string tag
        );

        public record Repository(
     int comment_count,
     int date_created,
     string description,
     string dockerfile,
     string full_description,
     bool is_official,
     bool is_private,
     bool is_trusted,
     string name,
     string @namespace,
     string owner,
     string repo_name,
     string repo_url,
     int star_count,
     string status
        );

        public record WebHookPost(
     string callback_url,
     PushData push_data,
     Repository repository
        );


    }
}
