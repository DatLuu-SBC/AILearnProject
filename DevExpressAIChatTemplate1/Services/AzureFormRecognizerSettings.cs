namespace DevExpressAIChatTemplate1.Services;

public class AzureFormRecognizerSettings
{
    public string? Endpoint { get; set; }
    public string? Key { get; set; }
    public string? ModelId { get; set; } = "prebuilt-layout";
}
