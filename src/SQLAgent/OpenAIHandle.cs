namespace SQLAgent;

public class OpenAIHandle : HttpClientHandler
{
    private static string UserAgent = "SQLAgent/" + typeof(OpenAIHandle).Assembly.GetName().Version?.ToString();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 重写user-agent
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(UserAgent);

        var response = await base.SendAsync(request, cancellationToken);
        return response;
    }
}