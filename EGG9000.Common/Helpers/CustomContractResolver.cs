using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace EGG9000.Common.Helpers {
    public class CustomContractResolver : DefaultContractResolver {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName.StartsWith("Has") || property.PropertyName == "Participants") {
                property.ShouldSerialize = i => false;
                property.Ignored = true;
            }

            return property;
        }
    }
}