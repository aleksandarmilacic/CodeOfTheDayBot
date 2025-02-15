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
    private static readonly string GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    private static readonly string OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private static readonly string GitHubUser = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY_OWNER");
    private static readonly string TargetRepo = "ai-generated-code-of-the-day";

    static async Task Main()
    {
       

        var github = new GitHubClient(new ProductHeaderValue("GitHubCommitBot"))
        {
            Credentials = new Credentials(GitHubToken)
        };

        var repo = await github.Repository.Get(GitHubUser, TargetRepo);
        await CommitCodeOfTheDay(github, repo);
    }

    static async Task CommitCodeOfTheDay(GitHubClient github, Repository repo)
    {
        string? code = await GenerateCodeWithChatGPT();
        if (string.IsNullOrEmpty(code))
        {
            Console.WriteLine("ChatGPT failed to generate code. Skipping commit.");
            return;
        }

        string fileName = $"CodeOfTheDay_{DateTime.UtcNow:yyyyMMdd}.cs";

        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
        var repoContent = await github.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name);

        var existingFile = repoContent.FirstOrDefault(f => f.Name == fileName);
        if (existingFile != null)
        {
            await github.Repository.Content.UpdateFile(repo.Owner.Login, repo.Name, fileName, new UpdateFileRequest($"Updating {fileName}", content, existingFile.Sha));
        }
        else
        {
            await github.Repository.Content.CreateFile(repo.Owner.Login, repo.Name, fileName, new CreateFileRequest($"Adding {fileName}", content, "main"));
        }

        Console.WriteLine($"Committed: {fileName} to {repo.Name}");
    }

    static async Task<string> GenerateCodeWithChatGPT()
    {
        string prompt = "Generate an interesting C# algorithm with comments that is runnable in a console application. Respond only in code, since the response in its full will be used as copy paste into the .cs file.";

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OpenAiApiKey);

        var payload = new
        {
            model = "gpt-4",
            messages = new[]
            {
            new { role = "system", content = "You are an expert C# developer providing creative and useful C# code snippets. Respond only in code, since the response in its full will be used as copy paste into the .cs file." },
            new { role = "user", content = prompt }
        },
            max_tokens = 400,
            temperature = 0.7
        };

        try
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", payload);
            response.EnsureSuccessStatusCode();

            string result = await response.Content.ReadAsStringAsync();
            var responseJson = JsonSerializer.Deserialize<OpenAiResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            string rawCode = responseJson?.Choices?[0]?.Message?.Content ?? "No response from ChatGPT.";

            return CleanCode(rawCode);
        }
        catch (Exception ex)
        {
            return $"Error calling ChatGPT API: {ex.Message}";
        }
    }

    // Method to clean up markdown formatting
    static string CleanCode(string rawCode)
    {
        if (string.IsNullOrEmpty(rawCode))
            return string.Empty;

        // Remove markdown triple backticks ```C# ... ``` or ``` ... ```
        rawCode = rawCode.Replace("```C#", "").Replace("```c#", "").Replace("```", "").Trim();

        return rawCode;
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
