using System.Text.Json;
public class TokenConfig
{
    public string Symbol { get; set; }
    public string Address { get; set; }
    public int Decimals { get; set; }
}

public class AppConfig
{
    public string infuraUrl { get; set; }
    public string Pkcs11LibraryPath { get; set; }
    public string PublicKeyDerPath { get; set; }
    public string DatabasePath { get; set; }
    public int KeyId { get; set; }
    public List<TokenConfig> Tokens { get; set; } = new();

    public static AppConfig Load(string path = "config.json")
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[ERROR] Config file not found: {Path.GetFullPath(path)}");
            Environment.Exit(1);
        }
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AppConfig>(json, options);
    }
}