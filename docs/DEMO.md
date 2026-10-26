# Demonstration guide

1. Double-click Run.cmd and open http://localhost:5188.
2. Enter the absolute path to samples/input as the source and samples/output as the output. Pick 800 × 600, maintain aspect ratio, and four workers.
3. Start the batch. Point out the worker cards, discovered counter, waiting queue, and result table.
4. Open a portrait result and explain how its aspect ratio fits inside the target bounds. Check that landscape and portrait subfolders appear under the output folder.
5. For a longer control demonstration, use a larger collection of your own images, set a larger custom size, then pause. Explain that workers reach checkpoints before pausing. Resume, then pause and stop to demonstrate cancellation while paused.
6. To demonstrate per-file errors, put a text file named broken.png in a disposable test source folder alongside a real image. The corrupt file should fail while the valid file completes.
7. Show ResizeEngine.cs and explain the shared Lock, queue, active-job counter, Task workers, pause gate, and CancellationTokenSource.
8. Run `dotnet test ParallelPixels.sln --configuration Release` to show the automated behavior checks.

Use a separate disposable output folder for demonstrations. Existing matching output files are replaced. Avoid using the included small sample batch to demonstrate Pause, since it can complete before a click reaches the server.
