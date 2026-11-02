using RetryLab;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5284");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var data = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? "data");
builder.Services.AddSingleton(new Store(Path.Combine(data, "operations.db")));
builder.Services.AddSingleton(new SimulatedReceiver(Path.Combine(data, "receiver.db")));
builder.Services.AddSingleton<Dispatcher>();
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    // Local demonstration only; reject cross-origin browser writes. No external integrations.
    if (ctx.Request.Method != "GET" && ctx.Request.Headers.TryGetValue("Origin", out var origin)
        && origin != $"{ctx.Request.Scheme}://{ctx.Request.Host}")
    { ctx.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (KeyConflictException) { await Results.Problem("A chave já foi usada com outro conteúdo.", statusCode: 409).ExecuteAsync(ctx); }
    catch (ArgumentException ex) { await Results.Problem(ex.Message, statusCode: 400).ExecuteAsync(ctx); }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok", mode = "simulation-only" }));
app.MapGet("/api/state", (Store store, SimulatedReceiver receiver) => new { operations = store.Operations(), events = store.Events(), history = store.History(), receipts = receiver.Receipts(), now = store.Now });
app.MapPost("/api/operations", (OperationRequest request, HttpContext ctx, Store store) =>
{
    var result = store.Create(ctx.Request.Headers["Idempotency-Key"].ToString(), request.Reference, request.AmountCents);
    return Results.Json(result, statusCode: result.Replayed ? 200 : 201);
});
app.MapPost("/api/dispatch", (DispatchRequest request, Dispatcher worker) =>
{
    if (!Enum.IsDefined(request.Mode)) return Results.BadRequest(new { error = "Modo inválido" });
    var item = worker.Tick(request.Mode); return Results.Ok(new { claimed = item is not null, eventId = item?.Id });
});
app.MapPost("/api/events/{id}/requeue", (string id, Store store) => store.Requeue(id) ? Results.Ok(new { requeued = true }) : Results.Conflict(new { error = "Somente eventos dead podem ser reabertos." }));
app.Run();
public record OperationRequest(string Reference, long AmountCents);
public record DispatchRequest(FailureMode Mode);
