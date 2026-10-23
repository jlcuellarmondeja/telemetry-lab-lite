namespace Telemetry.Application.Processing;

public enum ProcessingOutcome { Processed, Invalid }

// What happened to an event. The dispatcher decides ack vs quarantine from this.
public sealed record ProcessingResult(ProcessingOutcome Outcome, string? Reason = null)
{
    public static readonly ProcessingResult Ok = new(ProcessingOutcome.Processed);
    public static ProcessingResult Invalid(string reason) => new(ProcessingOutcome.Invalid, reason);
}
