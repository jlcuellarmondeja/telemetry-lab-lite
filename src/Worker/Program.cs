var builder = WebApplication.CreateBuilder(args);

// This is intentionally bare. The app runs and exposes /health out of the box
// so your container has something to probe. Everything else — the consumer,
// processing, persistence, shutdown handling — is yours to build.

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Build your worker from here. A BackgroundService that consumes from RabbitMQ
// (queue name: "telemetry.events") is the expected starting point, but the
// shape is up to you.

app.Run();
