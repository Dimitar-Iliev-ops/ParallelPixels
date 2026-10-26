using ParallelPixels.Web;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ResizeEngine>();
var app = builder.Build();
app.Use(async (context, next) => {
    if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "::1") ||
        (context.Request.Headers.Origin.FirstOrDefault() is string origin &&
         (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Authority != context.Request.Host.Value)))
    { context.Response.StatusCode = 403; return; }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/state", (ResizeEngine engine) => engine.Snapshot());
app.MapPost("/api/start", (ResizeRequest request, ResizeEngine engine) => {
    try { engine.Start(request); return Results.Ok(engine.Snapshot()); }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException)
    { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/{action}", (string action, ResizeEngine engine) => {
    if (action is not ("pause" or "resume" or "stop")) return Results.NotFound();
    engine.Control(action); return Results.Ok(engine.Snapshot());
});
app.MapGet("/api/results/{id:int}/image", (int id, ResizeEngine engine) => {
    var path = engine.ResultPath(id);
    return path is null ? Results.NotFound() : Results.File(path);
});
app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<ResizeEngine>().Control("stop"));
app.Run();
