using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoiseMap;

// ── Build ──────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<GeoIpService>();
builder.Services.AddHostedService<HoneypotService>();

// ── App ───────────────────────────────────────────────────────────────────
var app = builder.Build();

// Initialise GeoIP database at startup.
app.Services.GetRequiredService<GeoIpService>().Initialize();

// JSON options: camelCase to match the JavaScript client expectations.
var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
};

// ── WebSocket middleware ───────────────────────────────────────────────────
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    // No AllowedOrigins restriction — cross-origin connections from GoDaddy
    // are intentional.  The browser sends an Origin header but we don't gate on it.
});

// ── /ws endpoint ──────────────────────────────────────────────────────────
app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("WebSocket upgrade required.");
        return;
    }

    var bus = ctx.RequestServices.GetRequiredService<EventBus>();
    var log = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var ct  = ctx.RequestAborted;

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var subId    = bus.Subscribe(out var reader);
    var remote   = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

    log.LogInformation("WebSocket client connected from {IP}  ({N} total)",
        remote, bus.SubscriberCount);

    try
    {
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            if (ws.State != WebSocketState.Open) break;

            var json  = JsonSerializer.Serialize(evt, jsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    catch (WebSocketException ex)
    {
        log.LogDebug("WebSocket error for {IP}: {Msg}", remote, ex.Message);
    }
    finally
    {
        bus.Unsubscribe(subId);
        log.LogInformation("WebSocket client disconnected {IP}  ({N} remaining)",
            remote, bus.SubscriberCount);

        if (ws.State == WebSocketState.Open)
        {
            try
            {
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "Bye",
                    CancellationToken.None);
            }
            catch { /* ignore close errors */ }
        }
    }
});

// ── Health / stats probe ───────────────────────────────────────────────────
app.MapGet("/health", (EventBus bus) => new
{
    status      = "ok",
    subscribers = bus.SubscriberCount,
    time        = DateTimeOffset.UtcNow,
});

await app.RunAsync();
