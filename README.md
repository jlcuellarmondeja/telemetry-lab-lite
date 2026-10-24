# Telemetry Ingestion Worker (.NET 8)

Worker que consume eventos de telemetría de vehículos desde RabbitMQ (`telemetry.events`), los valida,
deduplica y persiste con reintentos, y mantiene un contador de eventos válidos por `deviceId`.
Expone `GET /health` y `GET /stats`.

```
RabbitMQ ──► Consumer (prefetch, ack manual)
               │  JSON roto / sin deviceId ─────────────────────────────► telemetry.events.quarantine
               ▼
          Dispatcher: hash(deviceId) % N ──► N colas acotadas, 1 lector fijo por cola
               ▼
          Validar ── fuera de rango ────────────────────────────────────► telemetry.events.quarantine
               ▼
          Dedupe por eventId ── ya visto ──► ack (no se cuenta)
               ▼
          Persistencia (retry + backoff + jitter ► circuit breaker) ── agotado ─► telemetry.events.dlq
               ▼
          Contador por dispositivo ──► ack                           /stats lee los contadores
```

## Estructura

| Proyecto | Responsabilidad |
|---|---|
| `Telemetry.Domain` | `TelemetryEvent` y `TelemetryValidator` (reglas puras, sin I/O). |
| `Telemetry.Application` | Puertos (`IDeduplicationStore`, `ITelemetryPersistence`, `IDeviceStatsStore`), `TelemetryProcessor`, `PartitionedDispatcher`, `PipelineStats`. |
| `Telemetry.Infrastructure` | RabbitMQ (conexión, consumer, cuarentena/DLQ), stores en memoria, decorador Polly de persistencia. |
| `Telemetry.Worker` | Composición (DI), `BackgroundService`, endpoints HTTP. |
| `Telemetry.Producer` | Productor proporcionado (sin cambios funcionales). |
| `tests/Telemetry.UnitTests` | Tests NUnit, sin broker. |

Dependencias: `Worker → Infrastructure → Application → Domain`. Application define los puertos,
Infrastructure los implementa y el Worker los compone.

## Cómo ejecutarlo

Requisitos: .NET 8 SDK y Docker Desktop (con Kubernetes activado en modo **kubeadm** para la parte k8s).

### Docker Compose (broker + producer + worker)

```bash
docker compose up --build
```

- Stats: http://localhost:8080/stats · Health: http://localhost:8080/health
- RabbitMQ UI: http://localhost:15672 (guest/guest, solo dev). Colas `telemetry.events.quarantine` y `telemetry.events.dlq`.
- Credenciales por variables `RABBITMQ_USER` / `RABBITMQ_PASS` (por defecto guest, solo para desarrollo local).

### Worker en local con `dotnet run`

```bash
docker compose up -d telemetry.rabbitmq
RabbitMq__UserName=guest RabbitMq__Password=guest dotnet run --project src/Telemetry.Worker
dotnet run --project src/Telemetry.Producer -- --rate 200 --devices 50
```

Sin credenciales el worker no arranca (`ValidateOnStart`): no hay valores por defecto en el código.

### Kubernetes (Docker Desktop)

```bash
scripts/build-images.cmd            # o ./scripts/build-images.sh → telemetryworker:1.0.0, telemetryproducer:1.0.0
kubectl create namespace telemetry
kubectl -n telemetry create secret generic rabbitmq-credentials --from-literal=username=telemetry --from-literal=password=dev-only-pass
kubectl -n telemetry create secret generic telemetry-worker-secrets --from-literal=RabbitMq__UserName=telemetry --from-literal=RabbitMq__Password=dev-only-pass
kubectl -n telemetry apply -f k8s/dev/rabbitmq.yaml
kubectl -n telemetry rollout status deploy/rabbitmq
kubectl -n telemetry apply -f k8s/configmap.yaml -f k8s/deployment.yaml -f k8s/service.yaml -f k8s/dev/producer.yaml
kubectl -n telemetry port-forward svc/telemetry-worker 8081:80   # http://localhost:8081/stats
```

- Probar el apagado ordenado (SIGTERM). El pod viejo se borra al terminar, así que hay que seguir sus logs **antes** de reiniciar:
  1. Terminal 1: `kubectl -n telemetry logs -f deploy/telemetry-worker`
  2. Terminal 2: `kubectl -n telemetry rollout restart deploy/telemetry-worker`
  3. En la terminal 1 aparece `Shutdown requested…` y después `All partitions drained in … ms`: el pod terminó lo que
     tenía en curso antes de salir, y lo no recibido queda en RabbitMQ para el pod nuevo.
- Comprobar no-root: `kubectl -n telemetry exec deploy/telemetry-worker -- id` → `uid=1654(app)`.
- Limpiar: `kubectl delete namespace telemetry`.

`k8s/secret.example.yaml` es solo una plantilla; los secretos reales se crean con `kubectl create secret` y nunca se commitean.
`k8s/dev/` (broker y producer) es solo para el clúster local.

### Tests

```bash
dotnet test Telemetry.slnx
```

## Decisiones de diseño

### 1. Concurrencia y paralelismo

- **Particionado por `deviceId`** (`PartitionedDispatcher`): `N` canales `System.Threading.Channels` acotados;
  partición = `hash(deviceId) % N`. Cada canal tiene **un único lector fijo**, así que los eventos de un mismo
  dispositivo se procesan en orden y los de dispositivos distintos en paralelo (hasta `N`).
- **Concurrencia acotada**: `N` tareas lectoras fijas (`Dispatcher__Partitions`, 8 por defecto), nunca un `Task` por mensaje.
- **Back-pressure de punta a punta**: canales con `FullMode = Wait` + `BasicQos(prefetch)` con ack manual. Si una
  partición se llena, el consumer espera y RabbitMQ deja de enviar (como máximo `PrefetchCount` mensajes sin ack en memoria).
- **Sin race conditions**: contadores con `ConcurrentDictionary` + `Interlocked`; dedupe con `TryAdd` atómico;
  `IModel` de RabbitMQ (no thread-safe) protegido con un lock para ack/publish desde varias particiones.
- Por qué no `ConsumerDispatchConcurrency > 1` ni `Parallel.ForEach`: dan paralelismo pero pierden el orden por dispositivo.

### 2. Tolerancia a fallos

- **Un fallo no mata ni bloquea el pipeline**: cada mensaje termina siempre en un destino explícito (ack, cuarentena o DLQ).
  Una excepción inesperada en una partición se captura, el mensaje va a la DLQ y el lector sigue.
- **Poison messages**: JSON inválido o sin `deviceId` se detecta en el consumer (ni siquiera se puede elegir partición);
  valores imposibles los detecta el validador. Ambos se publican en `telemetry.events.quarantine` con la cabecera
  `x-quarantine-reason` y se hace ack del original. **Nunca se reencolan.**
- **Graceful shutdown** (SIGTERM / Ctrl+C): `BasicCancel` (deja de aceptar) → se cierran los canales → los lectores
  drenan lo que tienen y hacen ack → salida. `HostOptions.ShutdownTimeout` = 25 s, por debajo de
  `terminationGracePeriodSeconds: 30` de k8s (y `stop_grace_period: 30s` en compose, cuyo valor por defecto es 10 s).
  Lo que quede sin ack lo redelivera RabbitMQ, y la deduplicación evita contarlo dos veces.
- **Broker no disponible al arrancar**: reintento de conexión con backoff exponencial + jitter (1 s … 30 s) en lugar de caerse.
  Las colas de cuarentena y DLQ se declaran explícitamente (no se usa DLX nativo porque el productor ya declara
  `telemetry.events` sin argumentos y cambiarlos daría `PRECONDITION_FAILED`).

### 3. Seguridad / desarrollo seguro

- **Idempotencia**: `InMemoryDeduplicationStore` (`ConcurrentDictionary<Guid, DateTimeOffset>` con TTL de 10 min y purga periódica).
  - `TryBegin` atómico: si dos copias del mismo `eventId` llegan a la vez, solo una gana.
  - El contador se incrementa **solo después** de persistir correctamente.
  - Si la persistencia falla definitivamente, se libera el `eventId` (`Release`), para que un reproceso desde la DLQ sí cuente.
  - Resultado: *at-least-once* de RabbitMQ + idempotencia = *effectively-once* dentro de la ventana de TTL.
  - Trade-offs: memoria acotada a cambio de una ventana finita (un duplicado que llegue tras el TTL se contaría);
    el estado se pierde al reiniciar y no se comparte entre réplicas.
- **Validación de entrada** (`TelemetryValidator`): `eventId` no vacío, `deviceId` presente y ≤ 64 caracteres,
  `fuelLevel ∈ [0,1]`, `speedKph ∈ [0,300]`, lat/lng en rango, timestamp no futuro (tolerancia de 5 min de deriva) ni de más de 7 días.
- **Sin secretos en el código**: opciones con `[Required]` + `ValidateOnStart`; credenciales por variables de entorno
  (`RabbitMq__UserName/Password`) desde un Secret de k8s. `appsettings.json` no contiene credenciales.
- **Contenedor no-root**: imagen `aspnet:8.0` con `USER $APP_UID` (1654). En k8s además `runAsNonRoot`,
  `readOnlyRootFilesystem` (con `emptyDir` en `/tmp`), `drop: [ALL]`, `allowPrivilegeEscalation: false`,
  seccomp `RuntimeDefault` y `automountServiceAccountToken: false`.

### 4. Políticas de reintento

Decorador `ResilientTelemetryPersistence` con Polly v8, de fuera hacia dentro:

1. **Retry**: 4 reintentos, backoff exponencial desde 200 ms (≈ 200/400/800/1600 ms) **con jitter** para que las
   particiones no reintenten sincronizadas. También reintenta si el circuito está abierto.
2. **Circuit breaker**: se abre si ≥ 50 % de fallos con al menos 20 llamadas en 10 s; queda abierto 15 s.
   Mientras está abierto, las llamadas fallan al instante sin tocar la dependencia.
3. Agotados los reintentos → el evento va a **`telemetry.events.dlq`** con la cabecera `x-dead-letter-reason`. No hay reintentos infinitos.

Todos los valores se configuran en la sección `Persistence`.

### Contenedor y despliegue

- **Dockerfile multi-stage**: SDK para compilar y `aspnet` para ejecutar. Primero se copian los `.csproj` y se hace
  `restore` (capa cacheable), `.dockerignore` excluye `bin/obj/.git`, usuario no-root, sin secretos en la imagen.
- **Deployment**: 1 réplica con estrategia `Recreate` (nunca hay dos consumidores a la vez, lo que mantiene el orden),
  tag de imagen fijo (nunca `:latest`), requests/limits, liveness y readiness sobre `/health`, `envFrom` ConfigMap + Secret,
  labels `app.kubernetes.io/*`, puerto con nombre.
- **Service** ClusterIP 80 → `http` (8080), para `/stats` y `/health` dentro del clúster.

### Bono: autoescalado

Para un consumidor de colas, la CPU es mala señal (un worker esperando a una dependencia lenta tiene CPU baja y
backlog alto). La señal correcta es la **profundidad de la cola**. `k8s/keda-scaledobject.example.yaml` +
`k8s/keda-triggerauth.yaml` configuran **KEDA** con el trigger de RabbitMQ (`QueueLength`): escala para mantener
~N mensajes pendientes por réplica, y puede bajar a 0 cuando la cola está vacía.

Está limitado a `maxReplicaCount: 1` a propósito. Con una sola cola compartida, dos pods romperían el orden por
dispositivo y la deduplicación en memoria (cada pod tendría la suya), y `/stats` mostraría solo lo de cada pod.
Escalar horizontalmente requiere lo descrito en "Trade-offs y qué haría con más tiempo".

## Librerías

| Librería | Por qué |
|---|---|
| `RabbitMQ.Client` 6.8.1 | La misma versión que usa el productor; API estable (`IModel`). La 7.x cambia a una API totalmente async. |
| `Polly.Core` 8 | Estándar en .NET para retry/circuit breaker; incluye Simmy (`AddChaosFault`) para inyectar fallos. |
| `System.Threading.Channels` | Colas acotadas con back-pressure, sin dependencias externas. |
| NUnit | Framework de tests; los tests no necesitan broker. |

## Suposiciones

- **La dependencia de persistencia "proporcionada" no existe en el scaffold.** Se implementa en memoria
  (`InMemoryTelemetryPersistence`, último evento por dispositivo) y el ~20 % de fallos se simula con Polly Simmy
  (`Persistence__FaultInjectionRate=0.2` en compose y k8s; 0 por defecto). Así no hay clases "falsas" en el código de producción.
- `fuelLevel` es una fracción `[0,1]` (el ejemplo del enunciado usa `0.43`). La velocidad máxima plausible es 300 km/h.
- El productor serializa en PascalCase y el enunciado en camelCase: se deserializa sin distinguir mayúsculas.
- Un evento que va a la DLQ ya no respeta el orden respecto a los siguientes de su dispositivo; se acepta, porque el
  contador es conmutativo.

## Tests (qué se prueba y por qué)

| Test | Requisito |
|---|---|
| `PartitionedDispatcherTests.SameDevice_IsProcessedInArrivalOrder` | 1: orden por dispositivo con varias particiones trabajando a la vez. |
| `PartitionedDispatcherTests.DifferentDevices_RunInParallel_ButNeverMoreThanPartitions` | 1: paralelismo real y acotado. |
| `PartitionedDispatcherTests.FailingAndPoisonEvents_AreSettled_AndPipelineKeepsGoing` | 2: un bug va a la DLQ, el poison a cuarentena y el resto sigue. |
| `PartitionedDispatcherTests.Complete_DrainsEverything_AndAcksEveryMessage` | 2: el drenado del apagado no pierde mensajes. |
| `TelemetryProcessorTests.SameEventIdTwice_IsCountedOnce` | 3: idempotencia ante redelivery. |
| `TelemetryProcessorTests.SameEventIdConcurrently_OnlyOneIsProcessed` | 3 + 1: 50 copias simultáneas, solo una cuenta (sin carreras). |
| `TelemetryProcessorTests.FailedSave_ReleasesEventId_SoARetryCountsIt` | 3: la dedupe no se "traga" un evento que falló. |
| `TelemetryProcessorTests.InvalidEvent_IsNotCounted` | 3: un evento inválido nunca llega al contador. |
| `TelemetryValidatorTests.*` | 3: los valores imposibles del productor (9999 km/h, fuel -1, +5 años) y que una deriva pequeña de reloj sí se acepte. |
| `ResilientTelemetryPersistenceTests.*` | 4: reintenta hasta tener éxito, se rinde tras el máximo y el breaker deja de llamar a la dependencia. |

Los tests son deterministas: reloj fijo (`FixedTimeProvider`), retardos de 0 y sin caos aleatorio.

## Trade-offs y qué haría con más tiempo

- **Escalado horizontal real**: particionar en el broker (*consistent-hash exchange* o *super streams* con
  *single active consumer* por partición) para que cada pod sea dueño de un subconjunto de dispositivos, y subir
  `maxReplicaCount` de KEDA.
- **Estado compartido y duradero**: dedupe en Redis (`SET eventId NX EX ttl`) o con una restricción única en base de datos,
  y contadores en Redis o base de datos. Hoy un reinicio vacía `/stats` y la ventana de dedupe (aceptable según el enunciado).
- **Persistencia y ack atómicos**: si el pod muere (sin apagado ordenado) entre persistir y hacer ack, el mensaje se
  redelivera a un proceso con la dedupe vacía. Una tabla de `eventId` procesados en la misma transacción que la escritura lo cierra.
- **Readiness real**: hoy `/health` solo indica que el proceso vive; un `/ready` que compruebe la conexión al broker y el estado del circuito.
- **Observabilidad**: métricas OpenTelemetry/Prometheus (lag de la cola, latencia por partición, estado del breaker) y trazas.
- **Reproceso de la DLQ**: herramienta o job para reinyectar mensajes una vez resuelto el problema.
- **RabbitMQ.Client 7.x** (API async) y quorum queues para durabilidad.
- **NUnit 4** y tests de integración con Testcontainers (RabbitMQ real), incluido el apagado con mensajes en vuelo.
