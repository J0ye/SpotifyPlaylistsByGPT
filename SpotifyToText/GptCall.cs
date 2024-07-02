using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

class GptCall
{
    private static readonly HttpClient client = new HttpClient();

    /*static async Task Main(string[] args)
    {
        string apiKey = "YOUR_OPENAI_API_KEY";
        string prompt = "Hello, how can I help you today?";

        var response = await GetGPT4Response(apiKey, prompt);
        Console.WriteLine(response);
    }*/

    public static async Task<string> GetGPT4Response(string apiKey, string prompt, string systemPrompt)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var requestData = new
        {
            model = "gpt-4",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prompt }
            }
        };
        Console.WriteLine("Asking gpt...");
        var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
        Console.WriteLine("Recieved message from gpt.");

        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var gptResponse = JsonConvert.DeserializeObject<GPTResponse>(responseBody);
            return gptResponse.choices[0].message.content;
        }
        else
        {
            Console.WriteLine("Error: " + responseBody);
            return null;
        }
    }
}

public class GPTResponse
{
    public Choice[] choices { get; set; }
}

public class Choice
{
    public Message message { get; set; }
}

public class Message
{
    public string role { get; set; }
    public string content { get; set; }
}