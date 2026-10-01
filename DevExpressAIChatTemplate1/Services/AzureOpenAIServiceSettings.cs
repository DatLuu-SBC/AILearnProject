namespace DevExpressAIChatTemplate1.Services;

public class AzureOpenAIServiceSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string DeploymentEmbeddingName { get; set; } = string.Empty;
}
