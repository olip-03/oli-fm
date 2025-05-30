using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Markdig;
using oli_fm.Struct;

namespace oli_fm;

public class ContentService
{
    private readonly string _repoUrl = "olip-03/oli-fm-content";
    private readonly HttpClient _httpClient;
    private readonly MarkdownPipeline _pipeline;

    public bool Loaded = false;
    public ConcurrentBag<Document> Documents = new();
    public ContentService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        _httpClient = new();
            
        // GitHub requires a User-Agent header
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CSharpApp");
        }
    }

    public async Task<RepositoryContent[]> GetRepositoryContentsAsync(string path = "")
    {
        string apiUrl =
            $"https://api.github.com/repos/{_repoUrl}/contents/{path}";

        HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
        response.EnsureSuccessStatusCode();
        string jsonResponse = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var contents = JsonSerializer.Deserialize<List<RepositoryContent>>(
            jsonResponse, options);
        return contents.ToArray();
    }
    
    public async Task<bool> UpdateDocuments(string path = "")
    {
        try
        {
            var contents = await GetRepositoryContentsAsync(path);
            var t = contents.Select(c => Task.Run(async () =>
            {
                Documents.Add(await ParseMarkdownFile(c));
            }));
            
            try
            {
                await Task.WhenAll(t);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }

            Loaded = true;
        }
        catch (UnauthorizedAccessException)
        {
            Trace.WriteLine($"Access denied to: {path}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error in directory {path}: {ex.Message}");
        }

        return true;
    }
    
    public async Task<Document> ParseMarkdownFile(RepositoryContent repoContent)
    {
        var response = await _httpClient.GetAsync(repoContent.GitUrl);
        response.EnsureSuccessStatusCode();
        var jsonResponse = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var blobData = JsonSerializer.Deserialize<JsonElement>(jsonResponse, options);
        var base64Content = blobData.GetProperty("content").GetString();

        // Remove GitHub's base64 formatting (but keep content newlines!) :3

        // Decode base64 content, nya~
        var content = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(base64Content));

        if (!content.StartsWith("---"))
        {
            return new Document(_pipeline)
            {
                Name = Path.GetFileNameWithoutExtension(repoContent.Name),
                Content = content,
                Tags = new string[0]
            };
        }

        var frontmatterEnd = content.IndexOf("\n---\n", 3);
        if (frontmatterEnd == -1)
            frontmatterEnd = content.IndexOf("\r\n---\r\n", 3);

        if (frontmatterEnd == -1)
            throw new InvalidOperationException("Invalid frontmatter format");

        var frontmatterYaml = content.Substring(3, frontmatterEnd - 3).Trim();
        var markdownContent = content.Substring(frontmatterEnd + 5).Trim();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var frontmatter = deserializer.Deserialize<FrontmatterModel>(frontmatterYaml);

        return new Document(_pipeline)
        {
            Name = frontmatter.Title ?? Path.GetFileNameWithoutExtension(repoContent.Name),
            Tags = frontmatter.Tags ?? new string[0],
            Content = markdownContent,
            Date = frontmatter.Date
        };
    }
}

