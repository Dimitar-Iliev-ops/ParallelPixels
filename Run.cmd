@echo off
cd /d "%~dp0"
echo ParallelPixels will run at http://localhost:5188
echo Keep this window open. Press Ctrl+C to stop the server.
dotnet run --project src/ParallelPixels.Web --no-launch-profile --urls http://localhost:5188
pause
