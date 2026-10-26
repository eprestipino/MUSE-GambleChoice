using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace GambleChoice
{
    // Shared by setup tools and M-USE's normal JsonConvert importer.
    public sealed class GambleConfigJson : JsonConverter
    {
        internal static readonly JsonSerializerSettings SettingsWithoutTrialSources = new JsonSerializerSettings
        {
            ContractResolver = new SettingsContractResolver()
        };

        public override bool CanWrite => false;
        public override bool CanConvert(Type objectType) => objectType == typeof(GambleConfig);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jsonReader = reader as JsonTextReader;
            if (jsonReader != null)
                jsonReader.DateParseHandling = DateParseHandling.None;
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException("Gamble configuration must be a JSON object.");
            var json = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            var config = new GambleConfig();
            using (var source = json.CreateReader())
                serializer.Populate(source, config);
            return config;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("Normal object serialization is used.");
        }

        public static GambleConfig Read(string json)
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                DateParseHandling = DateParseHandling.None
            });
            using (var text = new StringReader(json ?? ""))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
            {
                var root = JObject.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                if (reader.Read())
                    throw new JsonSerializationException("Unexpected content after the protocol.");
                return (root["Gamble"] ?? root).ToObject<GambleConfig>(serializer)
                    ?? throw new JsonSerializationException("Gamble configuration is null.");
            }
        }

        sealed class SettingsContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization)
            {
                var properties = base.CreateProperties(type, serialization);
                return type == typeof(GambleConfig)
                    ? properties.Where(p => p.PropertyName != "Options" && p.PropertyName != "Schedule" && p.PropertyName != "TrialTable").ToList()
                    : properties;
            }
        }
    }
}
