# ParallelPixels

ParallelPixels is a local Windows web application that resizes whole image collections using configurable parallel workers. It scans folders recursively, preserves the source folder structure in a separate output folder, and shows workers, queued images, finished files, and errors in a responsive dashboard.

Built for the **Concurrent Programming final exam project**. The repository includes the kickoff plan, a .NET solution, the working application, and automated tests.

## Run

Requires **Windows 10 or 11** and the **.NET 10 SDK**. Open `ParallelPixels.sln` in Visual Studio with .NET 10 support, or run:

```powershell
dotnet run --project src/ParallelPixels.Web --no-launch-profile --urls http://localhost:5188
```

Open **http://localhost:5188**. Alternatively, double-click `Run.cmd` on Windows.

1. Enter the full source and output folder paths on the computer running the server. Use separate folders that do not contain one another.
2. Pick a size preset or enter custom width and height, and choose 1, 2, 4, 8, or 16 workers.
3. Leave **Maintain aspect ratio** checked to fit images within the target bounds, or clear it to stretch to the exact dimensions.
4. Select **Start batch**. Pause, resume, and stop apply to all workers.
5. Watch the queue and worker cards. Click a completed filename to preview the output. Errors appear beside failed files.

Try the included `samples/input` folder first and use a separate `samples/output` folder. Input images are generated geometric test fixtures, with nested landscape and portrait folders. No personal files are included.

Existing output files with matching paths are replaced. Original files are never written by the application. Supported formats: JPEG, PNG, BMP, GIF, and TIFF. Animated or multipage images produce a single resized frame. EXIF orientation is applied; other metadata is not copied. Output format follows the file extension. WEBP is not supported.

## What one job is

One image job loads **one image**, calculates its output dimensions, resizes it, and writes one output file. Directory scan jobs enumerate **one folder** and add its subfolders and supported images to the same shared queue. This allows folder scanning and resizing to use the configured worker pool concurrently.

For example, a 3265 × 4898 portrait fitted inside 800 × 600 produces approximately **400 × 600**. A source file `input/portraits/photo.png` becomes `output/portraits/photo.png`.

## Planned features and implementation

All required features are implemented:

- Configurable pool of Task workers for both recursive scanning and resizing.
- Global Start, Stop, Pause, and Resume controls.
- Worker cards with the current state, file, and completed image count.
- Waiting queue, result table, individual errors, and image previews.
- Preset and custom dimensions with optional proportional resizing.
- Responsive browser UI with asynchronous requests and 500 ms state polling.
- Per-image failures do not terminate other workers.
- Cancellation cleans up temporary output files; completed output is retained.

## Shared data and synchronization

The worker pool shares a `Queue<Job>`, a result list, worker state records, counters, batch configuration, and lifecycle state. A single `System.Threading.Lock` protects all reads and writes to that shared state. The dashboard receives copies of the collections made while holding the same lock.

An active-job counter prevents workers from mistaking a temporarily empty queue for completion while another worker is still scanning a folder. Completion requires both an empty queue and no active jobs. Each queue removal is performed under the lock, so two workers cannot process the same queued entry.

Pause uses a `TaskCompletionSource` with asynchronous continuations. Workers await the shared gate between scan entries and image processing stages. Resume opens the gate. Stop cancels a shared `CancellationTokenSource` and releases paused workers. Image decoding, drawing, and saving are synchronous native operations inside background Tasks; cancellation and pause take effect at their next checkpoint. A running image operation can therefore finish after Pause is clicked.

No lock is held during image processing, file enumeration, asynchronous waits, or task joins. There is only one shared lock and no nested lock acquisition, avoiding inconsistent lock ordering and the usual deadlock cycle. See [the architecture notes](docs/ARCHITECTURE.md) for lifecycle and race-condition details.

## Verify

```powershell
dotnet build ParallelPixels.sln --configuration Release
dotnet test ParallelPixels.sln --configuration Release
```

The tests exercise multiworker processing, folder preservation, proportional sizing, exact stretching, output replacement, invalid paths and settings, empty batches, corrupt images, pause/resume, stop while paused, temporary-file cleanup, and restart. GitHub Actions runs the same build and tests on Windows.

## Repository layout

```text
src/ParallelPixels.Web/       ASP.NET Core API, worker engine, HTML/CSS/JavaScript UI
tests/ParallelPixels.Tests/   xUnit behavior tests using real generated image files
samples/input/               Generated sample images in nested folders
docs/                       Requirement mapping, architecture, demonstration guide
ParallelPixels.sln          Visual Studio solution
Run.cmd                     Local Windows launcher
```

## Scope and limits

This is a single-user local application, bound to localhost by the launcher. Folder paths refer to the server computer; it is not a hosted upload service. Cross-origin browser requests and unexpected hostnames are rejected. Directory junctions and symbolic links are skipped or rejected to keep traversal within real folders. Run against folders you control.

The UI displays the first 100 pending files and the latest 500 results to keep rendering responsive. Counters cover the whole batch; output files remain on disk. Queue and result metadata live in memory and reset when a new batch starts or the server exits. Stop reports discovered, unprocessed images as skipped; folders not yet scanned can contain additional undiscovered images. The app uses Microsoft's `System.Drawing.Common` and therefore requires Windows.

See [the demonstration guide](docs/DEMO.md) and [assignment checklist](docs/REQUIREMENTS.md).
