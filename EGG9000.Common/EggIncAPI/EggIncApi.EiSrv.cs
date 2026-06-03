using Ei;

using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public partial class EggIncApi {

        public static async Task<UserSubscriptionInfo> GetUserSubscription(string UserId) {
            var responseBytes = await PostRaw($"ei_srv/subscription_status/{UserId}", null, HeaderProfile.Ios);
            return responseBytes == null ? default : GetFromAuthenticatedMessage<UserSubscriptionInfo>(responseBytes);
        }
    }
}
