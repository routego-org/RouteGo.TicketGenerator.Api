namespace TicketGenerator.Common;

// ILambdaContext exposes RemainingTime, not a CancellationToken, so handlers have nothing to
// observe for cooperative cancellation. This adapts it to a real token that trips ~1s before
// Lambda's own timeout, leaving room to flush telemetry and return. (references/lambda-worker.md)
public static class LambdaContextExtensions
{
    public static CancellationToken RequestAborted(this ILambdaContext context)
    {
        return new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(1)).Token;
    }
}
