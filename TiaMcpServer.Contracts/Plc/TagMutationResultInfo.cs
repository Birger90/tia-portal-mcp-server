using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Plc;

public class TagMutationResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string FolderPath { get; set; } = "/";

    public string? TagName { get; set; }

    public string? UserConstantName { get; set; }

    /// <summary>
    /// create_tag/update_tag with <c>comment</c>: the culture of the <c>MultilingualTextItem</c> that was written (its
    /// <c>Language.Culture</c> name); absent when the item set no comment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommentCulture { get; set; }

    /// <summary>create_tag/update_tag with <c>comment</c>: the comment text read back from that item after writing it; absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comment { get; set; }
}
