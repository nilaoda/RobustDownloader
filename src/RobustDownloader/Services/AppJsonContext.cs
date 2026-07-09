using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using RobustDownloader.Models;

namespace RobustDownloader.Services;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ObservableCollection<DownloadTask>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CommandLineCommand))]
internal partial class AppJsonContext : JsonSerializerContext;
