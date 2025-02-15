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

namespace CodeOfTheDay;

class Program
{
    private static readonly string GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    private static readonly string OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private static readonly string GitHubUser = "your-github-username";
    private static readonly string TargetRepo = "AICodeOfTheDay";

    static async Task Main()
    {
        var random = new Random();

        // Chance to skip activity (~30%) for natural behavior
        if (random.Next(0, 100) < 30)
        {
            Console.WriteLine("Skipping today’s commit to make activity look human-like.");
            return;
        }

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

    static async Task<string?> GenerateCodeWithChatGPT()
    {
        var client = new RestClient("https://api.openai.com/v1/completions");
        var request = new RestRequest();
        request.Method = Method.Post;
        request.AddHeader("Authorization", $"Bearer {OpenAiApiKey}");
        request.AddHeader("Content-Type", "application/json");

        var prompt = "Generate a unique, fun, and executable C# code snippet for 'Code of the Day'. The code should be simple yet interesting. Include comments explaining its functionality.";

        var body = new
        {
            model = "gpt-4",
            prompt = prompt,
            max_tokens = 300,
            temperature = 0.7
        };

        request.AddJsonBody(body);

        var response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful || response.Content == null)
        {
            Console.WriteLine($"Error fetching from OpenAI: {response.StatusCode}");
            return null;
        }

        var jsonResponse = JsonDocument.Parse(response.Content);
        return jsonResponse.RootElement.GetProperty("choices")[0].GetProperty("text").GetString();
    }
}
