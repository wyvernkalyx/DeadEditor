using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Canonical JSON settings for the app's curation-data writers (box-set
    /// definitions, concert reference files). Centralizing them here keeps every
    /// persisted file the same shape — camelCase property names, indented,
    /// null values omitted — so app-written files match the bundled ones and
    /// cannot drift apart per-writer.
    ///
    /// Lifted from <c>BoxSetService</c>'s previously-private settings so both
    /// <c>BoxSetService</c> and the concert writer share one definition.
    /// </summary>
    public static class CanonicalJson
    {
        /// <summary>The shared serializer settings (camelCase, indented, ignore-null).</summary>
        public static JsonSerializerSettings Settings { get; } = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        /// <summary>Serializes a value to the canonical camelCase, indented form. Pure — no I/O.</summary>
        public static string Serialize(object value)
            => JsonConvert.SerializeObject(value, Settings);
    }
}
