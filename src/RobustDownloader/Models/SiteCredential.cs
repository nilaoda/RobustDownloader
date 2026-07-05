namespace RobustDownloader.Models;

public sealed class SiteCredential
{
    public bool Enabled { get; set; } = true;
    public string Pattern { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
