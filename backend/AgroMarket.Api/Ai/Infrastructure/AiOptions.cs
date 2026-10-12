namespace AgroMarket.Api.Ai.Infrastructure;

/// <summary>LLM 接入配置 (OpenAI 兼容)。ApiKey 只能来自项目配置或环境变量 Ai__ApiKey。</summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAICompat";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxTokens { get; set; } = 1024;
    public double Temperature { get; set; } = 0.3;

    /// <summary>配置是否完整可用。</summary>
    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}
