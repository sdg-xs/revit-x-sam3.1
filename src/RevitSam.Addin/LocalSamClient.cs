using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RevitSam.Addin;

internal sealed class LocalSamClient
{
    private static readonly HttpClient HealthHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly HttpClient DetectHttp = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly Uri BaseAddress = new("http://127.0.0.1:8767/");

    public async Task<JsonDocument> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HealthHttp.GetAsync(new Uri(BaseAddress, "health"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<JsonDocument> DetectAsync(string prompt, string imagePath, CancellationToken cancellationToken = default)
    {
        var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cancellationToken));
        var body = JsonSerializer.Serialize(new { prompt, image_base64 = imageBase64 });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await DetectHttp.PostAsync(new Uri(BaseAddress, "detect"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public Process? TryStartWorker()
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitSam31", "settings.json");
        if (!File.Exists(settingsPath))
            return null;

        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var root = settings.RootElement;
        var python = root.GetProperty("pythonExecutable").GetString();
        var startDirectory = root.GetProperty("workerDirectory").GetString();
        var samRepository = root.GetProperty("samRepository").GetString();

        if (string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(startDirectory) || string.IsNullOrWhiteSpace(samRepository)
            || !File.Exists(python) || !Directory.Exists(startDirectory) || !Directory.Exists(samRepository))
            return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            Arguments = "-m sam_worker",
            WorkingDirectory = startDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.Environment["SAM3_REPO"] = samRepository;
        var process = Process.Start(startInfo);
        if (process is null)
            return null;

        var logPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "worker.log");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(logPath, e.Data + Environment.NewLine); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(logPath, e.Data + Environment.NewLine); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
