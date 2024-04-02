using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace EGG9000.Site.Services {
    public class EmailSenderBlank : IEmailSender {
        #pragma warning disable 1998
        public async Task SendEmailAsync(string email, string subject, string htmlMessage) {
            return;
        }
    }
}
