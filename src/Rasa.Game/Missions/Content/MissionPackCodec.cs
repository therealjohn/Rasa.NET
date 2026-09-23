using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rasa.Game.Missions.Content
{
    public static class MissionPackCodec
    {
        public static JsonSerializerOptions Options { get; } = CreateOptions();
        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(type =>
            {
                if (type.Type.GetCustomAttribute<TableAttribute>() == null)
                    return;
                for (var index = type.Properties.Count - 1; index >= 0; index--)
                    if (type.Properties[index].AttributeProvider?.IsDefined(typeof(ColumnAttribute), true) != true)
                        type.Properties.RemoveAt(index);
            });
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                TypeInfoResolver = resolver,
                Converters = { new JsonStringEnumConverter() }
            };
        }
        public static MissionPackDocument Read(string json) =>
            JsonSerializer.Deserialize<MissionPackDocument>(json, Options)
            ?? throw new JsonException("Mission pack must be an object.");
        public static string Write(MissionPackDocument pack) => JsonSerializer.Serialize(pack, Options);
    }
}
