using Ei;
using System;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public sealed partial class EggIncApi {

        public async Task<ApiResult<UserSubscriptionInfo>> GetUserSubscription(string userId) {
            try {
                var (responseBytes, error) = await PostRawWithError($"ei_srv/subscription_status/{userId}", null, HeaderProfile.Ios);
                if(responseBytes == null)
                    return ApiResult<UserSubscriptionInfo>.Fail(error ?? "No response");
                return GetFromAuthenticatedMessage<UserSubscriptionInfo>(responseBytes);
            } catch(Exception e) {
                return ApiResult<UserSubscriptionInfo>.Fail("Bot Exception: " + e.Message);
            }
        }
    }
}
