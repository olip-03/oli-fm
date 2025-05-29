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
        _httpClient = new();
            
        // GitHub requires a User-Agent header
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CSharpApp");
        }
    }
    
    public async Task<bool> UpdateDocuments(string path = "")
    {
        try
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
            
            var t = contents.Select(c => Task.Run(async () =>
            {
                Documents.Add(await ParseMarkdownFile(c.DownloadUrl));
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
            // Sort and add to dict
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
    
    public async Task<Document> ParseMarkdownFile(string filePath)
    {
        var content = await _httpClient.GetStringAsync(filePath);
        
        if (!content.StartsWith("---"))
        {
            return new Document(_pipeline)
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
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
            Name = frontmatter.Title ?? Path.GetFileNameWithoutExtension(filePath),
            Tags = frontmatter.Tags ?? new string[0],
            Content = markdownContent,
            Date = frontmatter.Date
        };
    }
}

