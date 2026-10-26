# Concurrency architecture

The ASP.NET Core server owns one `ResizeEngine` singleton. HTTP handlers only validate commands, change lifecycle state, or copy a snapshot. Long-running work happens in background Tasks, so browser requests do not wait for batch completion.

## Worker lifecycle

`Idle → Running ↔ Paused → Stopping → Stopped`

Normal exhaustion changes `Running` to `Completed`. Unexpected engine failure changes it to `Failed`; individual file or directory failures are recorded in the results and the batch continues. A terminal state permits a new batch. Start is rejected while Running, Paused, or Stopping.

Each of N background workers takes a directory or image job from the shared queue. Directory jobs discover more work; image jobs produce files. Empty-queue workers wait briefly while another worker has an active job. A worker exits only when the queue is empty and the active count is zero. The coordinator awaits every worker before publishing a terminal state.

## Race conditions and prevention

| Potential race | Protection |
| --- | --- |
| Two workers dequeue the same image | Dequeue occurs under one shared Lock |
| Queue looks empty during directory enumeration | Active counter and empty check are read together under the Lock |
| UI enumerates a changing collection | Snapshot copies are produced under the Lock |
| Two Start requests overlap | State validation and initialization occur under the Lock |
| New batch overwrites state before old workers finish | Restart is forbidden until the coordinator has joined all workers |
| Stop leaves paused workers waiting forever | Cancellation interrupts gate waits and Stop releases the gate |
| Partial output appears as a completed image | Save to a unique temporary file, then replace destination after the save succeeds |

## Pause and cancellation

Pause swaps the completed gate for a new incomplete TaskCompletionSource. Workers await it before taking another job, before processing an image, before saving it, and between directory entries. Resume completes it. Asynchronous continuations keep resumed workers from executing inline under the lock.

Cancellation is cooperative. Decoding, drawing, and encoding are native synchronous calls within background Tasks and cannot be safely force-aborted. Stop waits for those calls to return, then removes temporary files at the next checkpoint. Already completed outputs remain available. The UI stays responsive while the batch is stopping.

## Deadlock avoidance

Only one lock protects application state. Workers release it before waiting, processing images, or doing file IO. The coordinator awaits Tasks without holding the lock. No worker synchronously waits on the UI thread, and the UI uses `fetch` rather than blocking requests.

## File behavior

Source and output trees must not overlap. Traversal skips reparse points. Destination directories are checked for junctions before writing. This is intended for trusted local folders, not hostile multiuser filesystems where another process changes paths during processing. Native decoding keeps an input stream open until drawing finishes. Every bitmap, graphics object, and stream is disposed after its job.
