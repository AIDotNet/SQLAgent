using System.Net;
using Microsoft.SemanticKernel;

namespace SQLAgent
{
    public static class KernelFactory
    {
        public static Kernel CreateKernel(string model, string apiKey, string endpoint,
            Action<IKernelBuilder>? kernelBuilderAction = null,
            string type = "OpenAI")
        {
            var kernelBuilder = Kernel.CreateBuilder();
            
            if (type == "OpenAI")
            {
                kernelBuilder.AddOpenAIChatCompletion(model, new Uri(endpoint), apiKey,
                    httpClient: new HttpClient(new OpenAIHandle()
                    {
                        // 启用压缩
                        AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip |
                                                 DecompressionMethods.Deflate
                    })
                    {
                        Timeout = TimeSpan.FromSeconds(600)
                    });
            }
            else if (type == "AzureOpenAI")
            {
                kernelBuilder.AddAzureOpenAIChatCompletion(model, endpoint, apiKey);
            }
            else
            {
                throw new NotSupportedException($"AI provider type '{type}' is not supported.");
            }

            kernelBuilderAction?.Invoke(kernelBuilder);

            var kernel = kernelBuilder.Build();


            return kernel;
        }
    }
}