# Assignment checklist

Based on the supplied kickoff assignment and final project requirements. This is an implementation mapping, not a claim about teacher approval or exam assessment.

| Requirement                                       | Delivery                                                       |
| ------------------------------------------------- | -------------------------------------------------------------- |
| Public GitHub repository with at least one commit | https://github.com/Dimitar-Iliev-ops/ParallelPixels            |
| .NET solution                                     | ParallelPixels.sln                                             |
| Visual Studio .NET gitignore                      | .gitignore generated from the dotnet template                  |
| README name and short description                 | README opening paragraphs                                      |
| Define one job and planned features               | README job and implementation sections                         |
| Shared resources and synchronization plan         | README and docs/ARCHITECTURE.md                                |
| Configurable parallel workers                     | Worker selector and ResizeEngine worker pool                   |
| Start Stop Pause Resume for all workers           | Dashboard buttons and shared engine lifecycle                  |
| Show current worker activity                      | Worker cards with current file and state                       |
| Show waiting jobs and finished jobs or errors     | Queue and results sections                                     |
| Responsive web application                        | ASP.NET Core background Tasks and asynchronous browser polling |
| Recursive local folder processing                 | Directory jobs enqueue subfolders and supported images         |
| Target sizes and custom dimensions                | Preset selector plus width and height inputs                   |
| Optional aspect ratio preservation                | Maintain aspect ratio checkbox                                 |
| Matching output paths and filenames               | Relative input paths reproduced under output root              |
| View result content                               | Click completed filenames for an image preview                 |

The chosen topic is the explicitly suggested Bulk Image Resizer. The kickoff only asks for an initial skeleton; this repository includes the working final application as well.

The user must provide the repository link to the teacher through the class submission channel. No school account, teacher contact, or submission destination was supplied.
