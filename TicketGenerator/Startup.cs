using OpenTelemetry.Trace;
using TicketGenerator.Infrastructure;
using TicketGenerator.Observability;
// FILLED BY skills-dotnet-lambda-vertical-slice — the usings for what it wires below (e.g. using TicketGenerator.Infrastructure.Pgsql;
// when the first data slice lands). Non-System usings only: System.* sort first (IDE0055).
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.SimpleNotificationService;

// How Lambda turns the SQS event JSON into SQSEvent and the batch response back into JSON.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace TicketGenerator;

// Composition root for the worker (references/lambda-worker.md). Explicit, boring, greppable:
// no reflection, no assembly scanning, no convention the debugger can't step through. The
// Amazon.Lambda.Annotations source generator builds the DI container from this [LambdaStartup]
// class. ConfigureHostBuilder() (not ConfigureServices) so AddObservability gets Configuration,
// Environment, and Logging — the web stacks get these from WebApplicationBuilder.
[LambdaStartup]
public sealed class Startup
{
    public HostApplicationBuilder ConfigureHostBuilder()
    {
        // Configuration reaches this worker through ONE channel in every environment: environment
        // variables, flat-named so there is no ':' / '__' separator to mistype (Infrastructure/
        // ConfigKeys.cs). The deployed values come from the deployment template; the local ones from
        // LocalEntryPoints, which MUST run before the builder is created — HostApplicationBuilder
        // snapshots the process environment block once (references/configuration.md).
        LocalEntryPoints.Apply();
        // FILLED BY skills-dotnet-lambda-vertical-slice when the first data slice lands — BEFORE the
        // builder, so nothing can query first: Dapper bakes its name mapping per (query, type) at first
        // execution, so a late call is a silent no-op (skills-db-postgres #11/#25). The timeout is
        // DERIVED from the function's own deadline (its timeout / the trigger's visibility timeout),
        // with the derivation in a why-comment here:
        //   DapperConfiguration.Apply(commandTimeout: TimeSpan.FromSeconds(...));

        var builder = new HostApplicationBuilder();

        // Fail at startup naming every missing key at once, rather than on whichever invocation
        // first needs one. FILLED BY the sibling slice skill as it introduces keys:
        //   builder.Configuration.RequireKeys(ConfigKeys.SomeQueueUrl);
        builder.Configuration.RequireKeys(ConfigKeys.TicketBucket, ConfigKeys.TicketTopicArn);

        // Log levels, in code for the same reason every other setting is: one place, and no
        // appsettings file to disagree with it. A deployed run can still be turned up without a
        // code change: Logging__LogLevel__Default is an ordinary environment variable.
        // Fully qualified: Amazon.Lambda.Core and Microsoft.Extensions.Logging both export a
        // LogLevel, and both are global usings on this stack (CS0104 otherwise).
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

        // Observability: vendor-neutral OpenTelemetry (traces + logs) over OTLP — baked in
        // (references/observability.md). Deployed -> ADOT Collector on localhost:4318; local dev ->
        // Otel:Exporter:* from user-secrets; unset -> graceful no-op. Unlike the web stacks there is
        // NO ASP.NET Core instrumentation and NO RequestTelemetryMiddleware — each [LambdaFunction]
        // opens its own root span (Diagnostics.ActivitySource) and force-flushes the injected
        // TracerProvider per invocation, because Lambda freezes between calls.
        builder.Services.AddObservability(
            builder.Configuration, builder.Environment, builder.Logging,
            configureTracing: tracing =>
            {
                tracing.AddProcessor<PiiRedactingProcessor>();
                // FILLED BY skills-dotnet-lambda-vertical-slice when the first data slice lands — the
                // database driver's spans, subscribed by SOURCE NAME so this file keeps no driver
                // reference and the architecture test's DB-driver rule holds (skills-db-postgres #20):
                //   tracing.AddSource("Npgsql");
            });

        // Secrets: standard AWS Secrets Manager access (references/secrets.md). ONE fixed name per
        // secret; the SOURCE is the environment — Development reads configuration, else Secrets Manager.
        builder.Services.AddSingleton<SecretResolver>();

        // --- slice registrations: ONE line per slice — the [LambdaFunction] entry (the Annotations
        //     generator resolves the containing type from the container). Grouped by Area,
        //     append-only. skills-dotnet-lambda-vertical-slice's new_slice.py appends the line; the
        //     handler and validator are NOT registered here — the function constructs them from
        //     injected ports at one call site. ---
        builder.Services.AddScoped<TicketGenerator.Features.Tickets.IssueTicket.IssueTicketFunction>();
        builder.Services.AddScoped<TicketGenerator.Features.Tickets.ReissueTicket.ReissueTicketFunction>();
        // --- end slice registrations ---

        // Infrastructure ports -> adapters, registered once. No per-slice handler/validator registrations.
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddAWSService<IAmazonS3>();
        builder.Services.AddAWSService<IAmazonSimpleNotificationService>();
        builder.Services.AddSingleton<ITicketRenderer, QuestPdfTicketRenderer>();
        builder.Services.AddSingleton<ITicketStore, S3TicketStore>();
        builder.Services.AddSingleton<ITicketAnnouncer, SnsTicketAnnouncer>();
        // FILLED BY skills-dotnet-lambda-vertical-slice when the first data slice lands:
        //   the reader/writer connection-port registrations + the worker's SystemActor
        //   (files copied from skills-db-postgres; all singletons) (+ any other ports).

        return builder;
    }
}
