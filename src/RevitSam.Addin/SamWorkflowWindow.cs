using System.Text.Json;
using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace RevitSam.Addin;

internal sealed class SamWorkflowWindow : Window
{
    private readonly UIApplication _uiApplication;
    private readonly LocalSamClient _client = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _prompt = new() { Text = "chair", MinWidth = 320, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _results = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Image _preview = new() { Height = 190, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 0) };

    public SamWorkflowWindow(UIApplication uiApplication)
    {
        _uiApplication = uiApplication;
        Title = "SAM 3.1 Asset Workflow";
        Width = 510;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer
        {
            Content = BuildContent(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Loaded += async (_, _) => await RefreshStatusAsync();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Local model status", FontSize = 16, FontWeight = FontWeights.SemiBold });
        _status.Text = "Checking local SAM 3.1 worker…";
        _status.Margin = new Thickness(0, 8, 0, 8);
        panel.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("Refresh status", async () => await RefreshStatusAsync()));
        buttons.Children.Add(MakeButton("Start worker", async () => await StartWorkerAsync()));
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = "Find assets in the active view", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) });
        panel.Children.Add(_prompt);
        panel.Children.Add(MakeButton("Capture view and detect", async () => await DetectActiveViewAsync()));
        panel.Children.Add(_results);
        panel.Children.Add(_preview);
        panel.Children.Add(new TextBlock
        {
            Text = "Preview only: model detections are 2D. Family placement will be enabled after view-to-model mapping and rotation review are implemented.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
        return panel;
    }

    private static Button MakeButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            using var health = await _client.GetHealthAsync();
            var root = health.RootElement;
            var device = root.TryGetProperty("gpu_name", out var gpu) && gpu.ValueKind == JsonValueKind.String
                ? gpu.GetString()
                : root.GetProperty("device").GetString();
            _status.Text = $"Worker connected · {root.GetProperty("status").GetString()} · device: {device}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Worker offline · {ex.GetBaseException().Message}";
        }
    }

    private async Task StartWorkerAsync()
    {
        try
        {
            using var existing = await _client.GetHealthAsync();
            _status.Text = "Local worker is already running.";
            return;
        }
        catch (HttpRequestException)
        {
            // No local worker is responding, so try to start the configured one.
        }
        catch (TaskCanceledException)
        {
            _status.Text = "Timed out checking the local worker.";
            return;
        }

        try
        {
            var process = _client.TryStartWorker();
            if (process is null)
            {
                _status.Text = "Worker not started. Configure scripts/configure-worker.ps1 first.";
                return;
            }
            _status.Text = "Starting local worker…";
            await Task.Delay(1500);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not start worker · {ex.GetBaseException().Message}";
        }
    }

    private async Task DetectActiveViewAsync()
    {
        var prompt = _prompt.Text.Trim();
        if (prompt.Length == 0)
        {
            _results.Text = "Enter a short object prompt first.";
            return;
        }

        var uiDocument = _uiApplication.ActiveUIDocument;
        if (uiDocument is null)
        {
            _results.Text = "Open a Revit project and view first.";
            return;
        }

        string? imagePath = null;
        try
        {
            var document = uiDocument.Document;
            var view = document.ActiveView;
            if (view.IsTemplate || view is ViewSchedule || view is ViewSheet)
            {
                _results.Text = "Choose a model view, not a template, schedule, or sheet.";
                return;
            }

            imagePath = Path.Combine(Path.GetTempPath(), $"revit-sam-{Guid.NewGuid():N}.png");
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.CurrentView,
                FilePath = imagePath,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1600,
            };
            document.ExportImage(options);

            using var response = await _client.DetectAsync(prompt, imagePath);
            var root = response.RootElement;
            var scores = root.GetProperty("scores");
            var boxes = root.GetProperty("boxes");
            var lines = new List<string>
            {
                $"Prompt: {prompt} · detections: {scores.GetArrayLength()} · image: {root.GetProperty("image_width")} × {root.GetProperty("image_height")}",
            };
            for (var i = 0; i < scores.GetArrayLength(); i++)
            {
                var box = boxes[i].EnumerateArray().Select(point => point.GetDouble()).ToArray();
                lines.Add($"{i + 1}. score {scores[i].GetDouble().ToString("P0", CultureInfo.InvariantCulture)} · box ({box[0]:F0}, {box[1]:F0})–({box[2]:F0}, {box[3]:F0}) px");
            }
            lines.Add(root.GetProperty("note").GetString() ?? string.Empty);
            _results.Text = string.Join(Environment.NewLine, lines);
            _preview.Source = DecodePreview(root.GetProperty("preview_base64").GetString());
        }
        catch (Exception ex)
        {
            _results.Text = $"Detection failed · {ex.GetBaseException().Message}";
        }
        finally
        {
            if (imagePath is not null && File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    private static BitmapImage? DecodePreview(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(Convert.FromBase64String(encoded));
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
