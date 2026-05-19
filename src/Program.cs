using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ClientModel;
using System.IO;
using System.Text;
using System.Text.Json;

if (args.Length < 2 || args[0] != "-p")
{
    throw new Exception("Usage: program -p <prompt>");
}

var prompt = args[1];

if (string.IsNullOrEmpty(prompt))
{
    throw new Exception("Prompt must not be empty");
}

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var baseUrl = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1";

if (string.IsNullOrEmpty(apiKey))
{
    throw new Exception("OPENROUTER_API_KEY is not set");
}

var client = new ChatClient(
    model: "anthropic/claude-haiku-4.5",
    credential: new ApiKeyCredential(apiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(baseUrl) }
);

var chatOptions = new ChatCompletionOptions();

chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
    functionName: "Read",
    functionDescription: "Read and return the contents of a file",
    functionParameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            file_path = new
            {
                type = "string",
                description = "The path to the file to read"
            }
        },
        required = new[] { "file_path" },
        additionalProperties = false
    })
));

chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
    functionName: "Write",
    functionDescription: "Write content to a file",
    functionParameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            file_path = new
            {
                type = "string",
                description = "The path of the file to write to"
            },
            content = new
            {
                type = "string",
                description = "The content to write to the file"
            }
        },
        required = new[] { "file_path", "content" },
        additionalProperties = false
    })
));

chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
    functionName: "Bash",
    functionDescription: "Execute a shell command",
    functionParameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            command = new
            {
                type = "string",
                description = "The command to execute"
            }
        },
        required = new[] { "command" },
        additionalProperties = false
    })
));

List<ChatMessage> messages = [new UserChatMessage(prompt)];

bool requiresAction = true;
while (requiresAction)
{
    ChatCompletion completion = client.CompleteChat(messages, chatOptions);
    
    // Add assistant's response to the conversation history
    messages.Add(new AssistantChatMessage(completion));
    
    if (completion.FinishReason == ChatFinishReason.ToolCalls)
    {
        foreach (ChatToolCall toolCall in completion.ToolCalls)
        {
            if (toolCall is ChatFunctionToolCall functionToolCall)
            {
                string functionName = functionToolCall.FunctionName;
                string toolCallId = functionToolCall.Id;
                string argumentsStr = functionToolCall.FunctionArguments;
                
                string result = "";
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(argumentsStr);
                    JsonElement root = doc.RootElement;
                    
                    if (functionName == "Read")
                    {
                        string filePath = root.GetProperty("file_path").GetString()!;
                        result = File.ReadAllText(filePath);
                    }
                    else if (functionName == "Write")
                    {
                        string filePath = root.GetProperty("file_path").GetString()!;
                        string content = root.GetProperty("content").GetString()!;
                        string? dir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllText(filePath, content);
                        result = $"Successfully wrote to {filePath}";
                    }
                    else if (functionName == "Bash")
                    {
                        string command = root.GetProperty("command").GetString()!;
                        using var process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = "bash";
                        process.StartInfo.ArgumentList.Add("-c");
                        process.StartInfo.ArgumentList.Add(command);
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        process.Start();
                        
                        if (process.WaitForExit(30000))
                        {
                            result = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                        }
                        else
                        {
                            process.Kill();
                            result = "Error: Process timed out after 30 seconds";
                        }
                    }
                }
                catch (Exception e)
                {
                    result = $"Error: {e.Message}";
                }
                
                messages.Add(new ToolChatMessage(toolCallId, result));
            }
        }
    }
    else
    {
        requiresAction = false;
        if (completion.Content != null && completion.Content.Count > 0)
        {
            Console.Write(completion.Content[0].Text);
        }
    }
}
// Dummy comment to trigger test build


