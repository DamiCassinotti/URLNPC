using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// The cloud arm of the selector comparison (issue #156): what the local models
// could not clear on latency and mode coverage together (#133) is asked of
// Claude through Anthropic's Messages API. Same ILlmEndpoint contract as
// OllamaEndpoint so LlmModeSelector never learns which side answered. Two
// differences a caller has to know about: LlmRequest.Seed is ignored — the
// Messages API has no seed parameter, so a run at a temperature above 0 is not
// reproducible here and the retry has to make the prompt different (which it
// already does) — and LlmRequest.JsonSchema is ignored too, since the plain
// Messages endpoint does not do schema-constrained decoding. LlmModeResponse's
// defensive parsing is the only line of defence, and the prompt's "JSON only"
// instruction is what leans on the model to produce something it can read.
public class AnthropicEndpoint : ILlmEndpoint
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string ApiVersion = "2023-06-01";
    public const string ApiKeyEnvVar = "ANTHROPIC_API_KEY";
    // Enough for the mode plus a short reason; longer answers wander and cost.
    internal const int MaxTokens = 256;

    [System.Serializable]
    class MessagesReply
    {
        public ContentBlock[] content;
        public ErrorBlock error;

        [System.Serializable]
        internal class ContentBlock { public string type; public string text; }
        [System.Serializable]
        internal class ErrorBlock { public string type; public string message; }
    }

    readonly string url;
    readonly string apiKey;

    public AnthropicEndpoint(string baseUrl, string apiKey)
    {
        string trimmed = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        url = trimmed + "/v1/messages";
        this.apiKey = apiKey ?? "";
    }

    public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<LlmCompletion>();
        if (cancellation.IsCancellationRequested)
        {
            completion.SetCanceled();
            return completion.Task;
        }
        // Reported as a completion failure rather than thrown so the driver
        // records it as a normal selector miss — a run with no key set would
        // otherwise die with an unobserved-task exception.
        if (string.IsNullOrEmpty(apiKey))
        {
            completion.SetResult(LlmCompletion.Failure(
                $"no API key: set the {ApiKeyEnvVar} environment variable"));
            return completion.Task;
        }

        byte[] body = System.Text.Encoding.UTF8.GetBytes(BuildBody(request));
        var web = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(1, request.TimeoutSeconds + 1),
        };
        web.SetRequestHeader("Content-Type", "application/json");
        web.SetRequestHeader("x-api-key", apiKey);
        web.SetRequestHeader("anthropic-version", ApiVersion);

        CancellationTokenRegistration registration = cancellation.Register(() =>
        {
            if (!web.isDone) web.Abort();
        });

        try
        {
            web.SendWebRequest().completed += operation =>
            {
                registration.Dispose();
                try
                {
                    if (cancellation.IsCancellationRequested) completion.TrySetCanceled();
                    else completion.TrySetResult(Interpret(web));
                }
                catch (System.Exception e)
                {
                    completion.TrySetException(e);
                }
                finally
                {
                    web.Dispose();
                }
            };
        }
        catch (System.Exception e)
        {
            registration.Dispose();
            web.Dispose();
            completion.TrySetResult(LlmCompletion.Failure($"could not send to {url}: {e.Message}"));
        }

        return completion.Task;
    }

    static LlmCompletion Interpret(UnityWebRequest web)
    {
        string text = web.downloadHandler != null ? web.downloadHandler.text : "";
        MessagesReply reply = TryParseReply(text);

        if (web.result != UnityWebRequest.Result.Success)
        {
            string apiMessage = reply != null && reply.error != null ? reply.error.message : "";
            string detail = string.IsNullOrEmpty(apiMessage) ? web.error : apiMessage;
            return LlmCompletion.Failure($"{detail} ({(int)web.responseCode})");
        }

        if (reply == null) return LlmCompletion.Failure("unreadable reply");
        if (reply.error != null && !string.IsNullOrEmpty(reply.error.message))
        {
            return LlmCompletion.Failure(reply.error.message);
        }
        if (reply.content == null || reply.content.Length == 0)
        {
            return LlmCompletion.Failure("no content in reply");
        }
        // Every text block joined: the API may split a long reply across them,
        // and dropping the tail would silently truncate the answer.
        var sb = new System.Text.StringBuilder();
        foreach (MessagesReply.ContentBlock block in reply.content)
        {
            if (block != null && block.type == "text" && !string.IsNullOrEmpty(block.text))
            {
                sb.Append(block.text);
            }
        }
        return LlmCompletion.Answer(sb.ToString());
    }

    static MessagesReply TryParseReply(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try { return JsonUtility.FromJson<MessagesReply>(text); }
        catch { return null; }
    }

    // Hand-built for the same reason as OllamaEndpoint.BuildBody: JsonUtility
    // would round-trip the schema and would not carry the raw prompt text
    // without a second layer of escaping.
    internal static string BuildBody(LlmRequest request)
    {
        var sb = new System.Text.StringBuilder(1024);
        sb.Append('{')
          .Append(JsonLine.Field("model", request.Model)).Append(',')
          .Append("\"max_tokens\":").Append(MaxTokens).Append(',')
          .Append(JsonLine.Field("temperature", request.Temperature)).Append(',')
          .Append("\"messages\":[{\"role\":\"user\",")
          .Append(JsonLine.Field("content", request.Prompt))
          .Append("}]}");
        return sb.ToString();
    }
}
