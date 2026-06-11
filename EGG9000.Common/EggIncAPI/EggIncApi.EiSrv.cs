using Ei;

using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public partial class EggIncApi {

        public static async Task<ApiResult<UserSubscriptionInfo>> GetUserSubscription(string UserId) {
            try {
                var (responseBytes, error) = await PostRawWithError($"ei_srv/subscription_status/{UserId}", null, HeaderProfile.Ios);
                if(responseBytes == null) {
                    return ApiResult<UserSubscriptionInfo>.Fail(error ?? "No response");
                }
                return GetFromAuthenticatedMessage<UserSubscriptionInfo>(responseBytes);
            } catch(System.Exception e) {
                return ApiResult<UserSubscriptionInfo>.Fail("Bot Exception: " + e.Message);
            }
        }
    }
}
