using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp;
using Octokit;
using System.Net.Http.Headers;
using System.Net;
using ProductHeaderValue = Octokit.ProductHeaderValue;
using System.Text.Json;
using System.Net.Http.Json;

namespace CodeOfTheDay;

class Program
{
    private static readonly string GitHubToken = Environment.GetEnvironmentVariable("GH_PAT");
    private static readonly string OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private static readonly string GitHubUser = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY_OWNER");
    private static readonly string TargetRepo = "ai-generated-code-of-the-day";
    private const string ReadMePath = "README.md";

    static async Task Main()
    {
        var github = new GitHubClient(new ProductHeaderValue("GitHubCommitBot"))
        {
            Credentials = new Credentials(GitHubToken)
        };

        var repo = await github.Repository.Get(GitHubUser, TargetRepo);

        // ✅ Fetch latest README.md before querying AI
        string readMeContent = await GetReadMeContent(github, repo);

        // ✅ Generate new unique C# code
        var (fileName, content) = await GenerateCodeWithChatGPT(readMeContent);
        if (string.IsNullOrEmpty(content))
        {
            Console.WriteLine("ChatGPT failed to generate code. Skipping commit.");
            return;
        }

        await CommitCodeOfTheDay(github, repo, fileName, content);
        await UpdateReadMe(github, repo, fileName);
    }

    static async Task<string> GetReadMeContent(GitHubClient github, Repository repo)
    {
        try
        {
            var readmeFile = await github.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, ReadMePath);
            return Encoding.UTF8.GetString(Convert.FromBase64String(readmeFile[0].Content));
        }
        catch (Octokit.NotFoundException)
        {
            Console.WriteLine("README.md not found. Creating a new one...");
            return "# AI Generated Code of the Day\n\n## History of Code Submissions\n";
        }
    }

    static async Task<(string fileName, string content)> GenerateCodeWithChatGPT(string readMeContent)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        string prompt = $"Today is {today}. Generate a unique C# algorithm based on today's date that is worthy of 'Code of the Day'. " +
                        "Avoid Fibonacci, Factorial, and common beginner algorithms. Instead, generate something creative, " +
                        "such as a leetcode-style problem, an interesting number pattern, or a mathematical puzzle.\n\n" +
                        "Your response **MUST** strictly follow this format:\n\n" +
                        "[fileName]UniqueAlgorithm_{DateTime.UtcNow:yyyyMMdd_HHmmss}.cs[/fileName]\n" +
                        "[code]\n<YOUR C# CODE HERE>\n[/code]\n\n" +
                        "Important: Do NOT repeat any of the following past algorithms:\n" + readMeContent;

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OpenAiApiKey);

        var payload = new
        {
            model = "gpt-4",
            messages = new[]
            {
                new { role = "system", content = "You are a strict AI that returns **ONLY PURE, EXECUTABLE C# CODE**. " +
                                                 "Absolutely NO comments, NO explanations, NO markdown, NO extra text. " +
                                                 "Each response must include:\n" +
                                                 "1. A unique filename inside [fileName][/fileName] tags.\n" +
                                                 "2. The pure C# code inside [code][/code] tags.\n" },
                new { role = "user", content = prompt }
            },
            max_tokens = 500,
            temperature = 0.9
        };

        try
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", payload);
            response.EnsureSuccessStatusCode();

            string result = await response.Content.ReadAsStringAsync();
            var responseJson = JsonSerializer.Deserialize<OpenAiResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            string rawResponse = responseJson?.Choices?[0]?.Message?.Content ?? "No response from ChatGPT.";

            return ExtractAlgorithmDetails(rawResponse);
        }
        catch (Exception ex)
        {
            return ($"Error_{DateTime.UtcNow:yyyyMMdd_HHmmss}.cs", $"Error calling ChatGPT API: {ex.Message}");
        }
    }

    static (string fileName, string code) ExtractAlgorithmDetails(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
            return ($"UnknownAlgorithm_{DateTime.UtcNow:yyyyMMdd_HHmmss}.cs", "No valid response received.");

        string fileName = "UnknownAlgorithm.cs";
        string code = "";

        var fileNameMatch = System.Text.RegularExpressions.Regex.Match(rawResponse, @"\[fileName\](.*?)\[/fileName\]");
        if (fileNameMatch.Success)
        {
            fileName = fileNameMatch.Groups[1].Value.Trim();
        }

        var codeMatch = System.Text.RegularExpressions.Regex.Match(rawResponse, @"\[code\](.*?)\[/code\]", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (codeMatch.Success)
        {
            code = codeMatch.Groups[1].Value.Trim();
        }

        return (fileName, code);
    }

    static async Task CommitCodeOfTheDay(GitHubClient github, Repository repo, string fileName, string content)
    {
        var repoContent = await github.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name);
        var existingFile = repoContent.FirstOrDefault(f => f.Name == fileName);

        if (existingFile != null)
        {
            await github.Repository.Content.UpdateFile(repo.Owner.Login, repo.Name, fileName,
                new UpdateFileRequest($"Updating {fileName}", content, existingFile.Sha));
        }
        else
        {
            await github.Repository.Content.CreateFile(repo.Owner.Login, repo.Name, fileName,
                new CreateFileRequest($"Adding {fileName}", content, "main"));
        }

        Console.WriteLine($"Committed: {fileName} to {repo.Name}");
    }

    static async Task UpdateReadMe(GitHubClient github, Repository repo, string fileName)
    {
        string readMeContent = await GetReadMeContent(github, repo);

        if (!readMeContent.Contains(fileName))
        {
            readMeContent += $"- [{fileName}](./{fileName}) ({DateTime.UtcNow:yyyy-MM-dd})\n";
        }

        string encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(readMeContent));
        var repoContent = await github.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, ReadMePath);

        if (repoContent.Any())
        {
            await github.Repository.Content.UpdateFile(repo.Owner.Login, repo.Name, ReadMePath,
                new UpdateFileRequest($"Updating README.md with {fileName}", encodedContent, repoContent[0].Sha));
        }
        else
        {
            await github.Repository.Content.CreateFile(repo.Owner.Login, repo.Name, ReadMePath,
                new CreateFileRequest($"Creating README.md and logging {fileName}", encodedContent, "main"));
        }

        Console.WriteLine($"Updated README.md with {fileName}");
    }
}

// OpenAI API Response Model
public class OpenAiResponse
{
    public Choice[] Choices { get; set; }
}

public class Choice
{
    public ChatMessage Message { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}
