# Telemetry Worker Lab (Lite) — Starter Scaffolding

A trimmed-down exercise scoped to **~3–4 hours**. We provide the message
producer, the broker, and a bare worker that runs and exposes `/health`.
Everything else is yours.

> Read `CANDIDATE_BRIEF.md` for the full requirements. This file only covers
> how to run the scaffolding.

## What's provided

```
src/
  Producer/      # Done for you. Pushes telemetry events to RabbitMQ.
  Worker/        # Bare: runs, exposes /health, has the event contract. Build the rest.
docker-compose.yml   # RabbitMQ + management UI.
```

You provide the `Dockerfile` and Kubernetes manifests (see the brief).

## Prerequisites

- .NET 8 SDK
- Docker + Docker Compose

## Run the infrastructure

```bash
docker compose up -d
# RabbitMQ management UI: http://localhost:15672  (guest / guest)
```

## Run the producer (event source)

```bash
cd src/Producer
dotnet run -- --rate 200 --devices 50
#   --rate            events per second (default 100)
#   --devices         number of distinct deviceIds (default 20)
#   --duplicate-rate  fraction resent as duplicates (default 0.05)
#   --poison-rate     fraction of malformed messages (default 0.02)
```

The producer deliberately emits **duplicates** and **poison messages** so you
can demonstrate idempotency and poison-message handling.

## Run the worker

```bash
cd src/Worker
dotnet run
# Health: GET http://localhost:8080/health
```

## Notes

- Queue name is `telemetry.events`.
- Broker credentials must come from env vars / config — **do not hardcode them.**
- The producer targets RabbitMQ; keep using it.
