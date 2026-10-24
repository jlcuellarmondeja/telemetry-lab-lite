namespace Telemetry.Infrastructure.Persistence;

// A failure that is worth retrying (timeout, connection reset...).
public sealed class TransientPersistenceException(string message) : Exception(message);
