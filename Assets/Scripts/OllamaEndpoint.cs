using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// The transport half of the LLM selector (issue #130): one POST to a local
// Ollama's /api/generate per decision, with the answer constrained to the mode
// schema. UnityWebRequest rather than HttpClient because this has to work in a
// -batchmode player, and it is created and sent on the main thread — the
// driver's calling thread — with the completion awaited off a
// TaskCompletionSource so no frame ever blocks on it.
public class OllamaEndpoint : ILlmEndpoint
{
    // Ollama's own reply envelope; the model's text is the "response" field,
    // and "error" is what it sends for an unknown model or a bad request.
    [System.Serializable]
    class GenerateReply
    {
        public string response;
        public string error;
    }

    readonly string url;

    public OllamaEndpoint(string generateUrl)
    {
        url = generateUrl;
    }

    public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<LlmCompletion>();
        if (cancellation.IsCancellationRequested)
        {
            completion.SetCanceled();
            return completion.Task;
        }

        byte[] body = System.Text.Encoding.UTF8.GetBytes(BuildBody(request));
        var web = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer(),
            // Backstop only: the selector owns the real timeout, this just keeps
            // a dropped connection from holding a socket for minutes.
            timeout = Mathf.Max(1, request.TimeoutSeconds + 1),
        };
        web.SetRequestHeader("Content-Type", "application/json");

        // Abort has to happen on the main thread, which is where every
        // cancellation comes from: the driver's FixedUpdate, or the selector's
        // own timeout resuming on Unity's synchronization context.
        CancellationTokenRegistration registration = cancellation.Register(() =>
        {
            // Abort makes the request complete as a connection error; the
            // TrySet below is what keeps that from being reported as one.
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
            // A malformed endpoint is the likely one, and it would otherwise
            // leak a request and a registration per decision.
            registration.Dispose();
            web.Dispose();
            completion.TrySetResult(LlmCompletion.Failure($"could not send to {url}: {e.Message}"));
        }

        return completion.Task;
    }

    static LlmCompletion Interpret(UnityWebRequest web)
    {
        if (web.result != UnityWebRequest.Result.Success)
        {
            return LlmCompletion.Failure($"{web.error} ({(int)web.responseCode})");
        }

        string text = web.downloadHandler.text;
        GenerateReply reply;
        try
        {
            reply = JsonUtility.FromJson<GenerateReply>(text);
        }
        catch (System.Exception e)
        {
            return LlmCompletion.Failure($"unreadable reply: {e.Message}");
        }
        if (reply == null) return LlmCompletion.Failure("empty reply");
        if (!string.IsNullOrEmpty(reply.error)) return LlmCompletion.Failure(reply.error);
        return LlmCompletion.Answer(reply.response);
    }

    // Hand-built rather than JsonUtility.ToJson: "format" carries a raw JSON
    // schema, which a serialized string field would escape into a string.
    internal static string BuildBody(LlmRequest request)
    {
        var sb = new System.Text.StringBuilder(1024);
        sb.Append('{')
          .Append(JsonLine.Field("model", request.Model)).Append(',')
          .Append(JsonLine.Field("prompt", request.Prompt)).Append(',')
          .Append("\"stream\":false,");
        if (!string.IsNullOrEmpty(request.JsonSchema))
        {
            sb.Append("\"format\":").Append(request.JsonSchema).Append(',');
        }
        sb.Append("\"options\":{")
          .Append(JsonLine.Field("temperature", request.Temperature)).Append(',')
          .Append(JsonLine.Field("seed", request.Seed))
          .Append("}}");
        return sb.ToString();
    }
}
