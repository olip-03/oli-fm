using Markdig;

namespace oli_fm.Struct;

public class Document
{
    private MarkdownPipeline _pipelineRef;

    public string Name { get; set; }
    public string[] Tags { get; set; }
    public string Content { get; set; }
    public DateTime? Date { get; set; }

    public Document(MarkdownPipeline pipeline)
    {
        _pipelineRef = pipeline;
    }

    public string ToHtml()
    {
        return Markdown.ToHtml(Content, _pipelineRef);
    }
}