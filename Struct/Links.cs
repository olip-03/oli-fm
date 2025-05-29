using System.Text.Json.Serialization;

namespace oli_fm.Struct;

public class Links
{
    [JsonPropertyName("self")]
    public string Self { get; set; }

    [JsonPropertyName("git")]
    public string Git { get; set; }

    [JsonPropertyName("html")]
    public string Html { get; set; }
}

