using EGG9000.Common.Services;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class SlashCommandExtensions {
        public static async Task DeleteResponseFix(this FauxCommand command) {
            if(command == null)
                return;
            var response = await command.GetOriginalResponseAsync();
            if(response == null)
                return;
            await response.DeleteAsync();
        }
    }
}
