using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
namespace ParallelPixels.Web;

public record ResizeRequest(string Source, string Output, int Width = 800, int Height = 600, int Workers = 4, bool MaintainAspectRatio = true);
public record Job(string Path, bool Directory);
public record WorkerInfo(int Id, string State, string? File, int Completed);
public record JobResult(int Id, string File, string Status, int Width, int Height, long Bytes, long Milliseconds, string? Error);

public sealed class ResizeEngine
{
    private readonly Lock sync = new();
    private readonly Queue<Job> queue = new();
    private readonly List<JobResult> results = [];
    private readonly Dictionary<int, string> resultPaths = [];
    private WorkerInfo[] workers = [];
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource resume = NewGate();
    private ResizeRequest? request;
    private string status = "Idle";
    private int active, discovered, scanned, skipped;
    private DateTimeOffset? started, ended;
    private string? fatalError;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff" };
    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Start(ResizeRequest options)
    {
        if (string.IsNullOrWhiteSpace(options.Source) || string.IsNullOrWhiteSpace(options.Output)) throw new ArgumentException("Choose source and output folders.");
        if (options.Width is < 1 or > 8192 || options.Height is < 1 or > 8192) throw new ArgumentException("Dimensions must be between 1 and 8192 pixels.");
        if (options.Workers is < 1 or > 16) throw new ArgumentException("Choose between 1 and 16 workers.");
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Source));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Output));
        if (!Directory.Exists(source)) throw new ArgumentException("The source folder does not exist.");
        if (Inside(source, output) || Inside(output, source)) throw new ArgumentException("Source and output folders must be separate and cannot contain each other.");
        if (HasLink(source) || HasLink(output)) throw new ArgumentException("Use real folders rather than symbolic links or junctions.");
        lock (sync)
        {
            if (status is "Running" or "Paused" or "Stopping") throw new InvalidOperationException("Stop the current batch before starting another.");
            Directory.CreateDirectory(output);
            cancellation?.Dispose(); cancellation = new();
            request = options with { Source = source, Output = output };
            queue.Clear(); results.Clear(); resultPaths.Clear();
            active = discovered = scanned = skipped = 0; fatalError = null;
            started = DateTimeOffset.UtcNow; ended = null;
            resume = NewGate(); resume.TrySetResult();
            queue.Enqueue(new(source, true));
            workers = Enumerable.Range(1, options.Workers).Select(id => new WorkerInfo(id, "Starting", null, 0)).ToArray();
            status = "Running";
            var batch = request; var token = cancellation.Token;
            _ = Task.Run(() => RunAsync(batch, token));
        }
    }
    private static bool Inside(string child, string parent) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool HasLink(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    public void Control(string action)
    {
        lock (sync)
        {
            if (action == "pause" && status == "Running") { status = "Paused"; resume = NewGate(); }
            if (action == "resume" && status == "Paused") { status = "Running"; resume.TrySetResult(); }
            if (action == "stop" && status is "Running" or "Paused") { status = "Stopping"; cancellation!.Cancel(); resume.TrySetResult(); }
        }
    }
    private async Task RunAsync(ResizeRequest options, CancellationToken token)
    {
        try { await Task.WhenAll(Enumerable.Range(0, options.Workers).Select(id => Task.Run(() => WorkAsync(id, options, token), CancellationToken.None))); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { lock (sync) fatalError = ex.Message; }
        finally
        {
            lock (sync)
            {
                skipped += queue.Count(j => !j.Directory); queue.Clear();
                status = fatalError is not null ? "Failed" : token.IsCancellationRequested ? "Stopped" : "Completed";
                ended = DateTimeOffset.UtcNow;
                for (var i = 0; i < workers.Length; i++) workers[i] = workers[i] with { State = "Idle", File = null };
            }
        }
    }
    private async Task WaitAsync(int id, CancellationToken token)
    {
        Task gate;
        string previous;
        lock (sync) { gate = resume.Task; previous = workers[id].State; if (!gate.IsCompleted) workers[id] = workers[id] with { State = "Paused" }; }
        await gate.WaitAsync(token); token.ThrowIfCancellationRequested();
        lock (sync) if (workers[id].State == "Paused") workers[id] = workers[id] with { State = previous };
    }
    private async Task WorkAsync(int id, ResizeRequest options, CancellationToken token)
    {
        while (true)
        {
            await WaitAsync(id, token);
            Job? job;
            lock (sync)
            {
                if (status == "Paused") continue;
                if (!queue.TryDequeue(out job))
                {
                    if (active == 0) return;
                    workers[id] = workers[id] with { State = "Waiting", File = null };
                }
                else { active++; workers[id] = workers[id] with { State = job.Directory ? "Scanning" : "Resizing", File = Path.GetRelativePath(options.Source, job.Path) }; }
            }
            if (job is null) { await Task.Delay(25, token); continue; }
            var timer = Stopwatch.StartNew();
            try
            {
                if (job.Directory)
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(job.Path))
                    {
                        await WaitAsync(id, token);
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        if (!isDirectory && !Extensions.Contains(Path.GetExtension(entry))) continue;
                        lock (sync) { queue.Enqueue(new(entry, isDirectory)); if (!isDirectory) discovered++; }
                    }
                    lock (sync) scanned++;
                }
                else
                {
                    await using var input = new FileStream(job.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    using var image = Image.FromStream(input, false, true);
                    await WaitAsync(id, token);
                    Orient(image);
                    var ratio = Math.Min((double)options.Width / image.Width, (double)options.Height / image.Height);
                    var width = options.MaintainAspectRatio ? Math.Max(1, (int)Math.Round(image.Width * ratio)) : options.Width;
                    var height = options.MaintainAspectRatio ? Math.Max(1, (int)Math.Round(image.Height * ratio)) : options.Height;
                    using var resized = new Bitmap(width, height);
                    using (var graphics = Graphics.FromImage(resized))
                    {
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(image, new Rectangle(0, 0, width, height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                    }
                    await WaitAsync(id, token);
                    var destination = Path.Combine(options.Output, Path.GetRelativePath(options.Source, job.Path));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (HasLink(Path.GetDirectoryName(destination)!) || (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)) throw new IOException("Output contains a symbolic link or junction.");
                    var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        resized.Save(temporary, OutputFormat(destination));
                        token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    lock (sync)
                    {
                        var resultId = results.Count;
                        results.Add(new(resultId, Path.GetRelativePath(options.Source, job.Path), "Done", width, height, new FileInfo(destination).Length, timer.ElapsedMilliseconds, null));
                        resultPaths[resultId] = destination;
                    }
                }
                lock (sync) workers[id] = workers[id] with { Completed = workers[id].Completed + (job.Directory ? 0 : 1) };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { lock (sync) { if (!job.Directory) skipped++; } throw; }
            catch (Exception ex) { lock (sync) results.Add(new(results.Count, Path.GetRelativePath(options.Source, job.Path), "Failed", 0, 0, 0, timer.ElapsedMilliseconds, ex.Message)); }
            finally { lock (sync) { active--; workers[id] = workers[id] with { State = "Waiting", File = null }; } }
        }
    }
    public object Snapshot()
    {
        lock (sync) return new
        {
            status, started, ended, discovered, scanned, skipped, active, error = fatalError,
            completed = results.Count(r => r.Status == "Done"), failed = results.Count(r => r.Status == "Failed"),
            pendingCount = queue.Count(j => !j.Directory),
            pending = queue.Where(j => !j.Directory).Take(100).Select(j => Path.GetRelativePath(request!.Source, j.Path)).ToArray(),
            workers = workers.ToArray(), results = results.TakeLast(500).Reverse().ToArray()
        };
    }
    public string? ResultPath(int id) { lock (sync) return resultPaths.GetValueOrDefault(id); }
    private static ImageFormat OutputFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImageFormat.Jpeg, ".gif" => ImageFormat.Gif,
        ".bmp" => ImageFormat.Bmp, ".tif" or ".tiff" => ImageFormat.Tiff, _ => ImageFormat.Png
    };
    private static void Orient(Image image)
    {
        if (!image.PropertyIdList.Contains(0x112)) return;
        var value = image.GetPropertyItem(0x112)?.Value;
        if (value is null || value.Length < 2) return;
        image.RotateFlip(BitConverter.ToUInt16(value, 0) switch
        {
            2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone
        });
    }
}

