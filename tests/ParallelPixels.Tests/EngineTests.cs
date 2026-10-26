using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ParallelPixels.Web;

namespace ParallelPixels.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ParallelPixels-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ResizeEngine engine = new();
    private string Source => Path.Combine(root, "input");
    private string Output => Path.Combine(root, "output");
    public EngineTests() => Directory.CreateDirectory(Source);
    private JsonElement State() => JsonSerializer.SerializeToElement(engine.Snapshot());
    private async Task<JsonElement> Until(Func<JsonElement, bool> predicate, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var state = State(); if (predicate(state)) return state;
            await Task.Delay(20);
        }
        throw new TimeoutException(State().ToString());
    }
    private void ImageFile(string relative, int width = 120, int height = 240)
    {
        var path = Path.Combine(Source, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        bitmap.Save(path, ImageFormat.Png);
    }
    [Fact]
    public async Task PreservesStructureAndAspectRatioWithEightWorkers()
    {
        for (var i = 0; i < 40; i++) ImageFile($"folder{i % 4}/photo{i}.png");
        engine.Start(new(Source, Output, 80, 60, 8));
        var state = await Until(s => s.GetProperty("status").GetString() == "Completed");
        Assert.Equal(40, state.GetProperty("completed").GetInt32());
        Assert.Equal(0, state.GetProperty("failed").GetInt32());
        Assert.Equal(8, state.GetProperty("workers").GetArrayLength());
        Assert.Equal(40, Directory.GetFiles(Output, "*.png", SearchOption.AllDirectories).Length);
        using var result = Image.FromFile(Path.Combine(Output, "folder0/photo0.png"));
        Assert.Equal((30, 60), (result.Width, result.Height));
        Assert.Equal(40, state.GetProperty("workers").EnumerateArray().Sum(w => w.GetProperty("Completed").GetInt32()));
        Assert.Equal(40, state.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("Id").GetInt32()).Distinct().Count());
    }
    [Fact]
    public async Task StretchUsesExactDimensionsAndReplacesOutput()
    {
        ImageFile("one.png");
        engine.Start(new(Source, Output, 80, 60, 1, false));
        await Until(s => s.GetProperty("status").GetString() == "Completed");
        using (var result = Image.FromFile(Path.Combine(Output, "one.png"))) Assert.Equal((80, 60), (result.Width, result.Height));
        engine.Start(new(Source, Output, 25, 25, 2, false));
        await Until(s => s.GetProperty("status").GetString() == "Completed");
        using var replaced = Image.FromFile(Path.Combine(Output, "one.png"));
        Assert.Equal((25, 25), (replaced.Width, replaced.Height));
    }
    [Fact]
    public async Task CorruptImageDoesNotStopOtherJobs()
    {
        ImageFile("valid.png"); await File.WriteAllTextAsync(Path.Combine(Source, "broken.png"), "not an image");
        await File.WriteAllTextAsync(Path.Combine(Source, "ignored.txt"), "ignore me");
        engine.Start(new(Source, Output));
        var state = await Until(s => s.GetProperty("status").GetString() == "Completed");
        Assert.Equal(1, state.GetProperty("completed").GetInt32());
        Assert.Equal(1, state.GetProperty("failed").GetInt32());
        Assert.Equal(2, state.GetProperty("discovered").GetInt32());
        Assert.Empty(Directory.GetFiles(Output, "*.tmp", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task EmptyFolderFinishes()
    {
        engine.Start(new(Source, Output));
        var state = await Until(s => s.GetProperty("status").GetString() == "Completed");
        Assert.Equal(0, state.GetProperty("completed").GetInt32());
    }
    [Fact]
    public void RejectsUnsafeFoldersAndInvalidSettings()
    {
        Assert.Throws<ArgumentException>(() => engine.Start(new(Source, Source)));
        Assert.Throws<ArgumentException>(() => engine.Start(new(Source, Path.Combine(Source, "nested"))));
        Assert.Throws<ArgumentException>(() => engine.Start(new(Source, root)));
        Assert.Throws<ArgumentException>(() => engine.Start(new(Source, Output, Workers: 0)));
        Assert.Throws<ArgumentException>(() => engine.Start(new(Source, Output, Width: 0)));
        Assert.Throws<ArgumentException>(() => engine.Start(new(Path.Combine(root, "missing"), Output)));
    }
    [Fact]
    public async Task PauseResumeStopAndRestartAreCooperative()
    {
        ImageFile("seed.png");
        for (var i = 0; i < 160; i++) File.Copy(Path.Combine(Source, "seed.png"), Path.Combine(Source, $"copy{i}.png"));
        engine.Start(new(Source, Output, 1600, 1200, 4));
        engine.Control("pause");
        var paused = await Until(s => s.GetProperty("status").GetString() == "Paused" && s.GetProperty("workers").EnumerateArray().All(w => w.GetProperty("State").GetString() == "Paused"));
        var count = paused.GetProperty("completed").GetInt32();
        await Task.Delay(150);
        Assert.Equal(count, State().GetProperty("completed").GetInt32());
        Assert.Throws<InvalidOperationException>(() => engine.Start(new(Source, Output)));
        engine.Control("resume");
        await Until(s => s.GetProperty("completed").GetInt32() > count);
        engine.Control("pause"); engine.Control("stop");
        var stopped = await Until(s => s.GetProperty("status").GetString() == "Stopped");
        Assert.Equal(0, stopped.GetProperty("active").GetInt32());
        Assert.Empty(Directory.GetFiles(Output, "*.tmp", SearchOption.AllDirectories));
        engine.Start(new(Source, Output, 8, 8, 8));
        var completed = await Until(s => s.GetProperty("status").GetString() == "Completed");
        Assert.Equal(161, completed.GetProperty("completed").GetInt32());
    }
    public void Dispose() { engine.Control("stop"); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
