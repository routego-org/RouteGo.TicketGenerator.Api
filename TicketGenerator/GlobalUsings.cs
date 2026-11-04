// Slice files carry no using directives — this file holds the shared set.
// The sibling slice skill (trigger event packages, FluentValidation, Common/ types) and
// database setup (Dapper, System.Data.Common) EXTEND this list beneath the '// FILLED BY' marker below.
global using System.Text.Json;
global using Amazon.Lambda.Annotations;
global using Amazon.Lambda.Core;          // ILambdaContext
global using Microsoft.Extensions.Configuration;        // plain SDK lacks the web-SDK implicit usings
global using Microsoft.Extensions.DependencyInjection;   // that SecretResolver / Observability / Startup rely on
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using TicketGenerator.Common;             // LambdaContextExtensions, HttpClients, WireJson
global using TicketGenerator.Common.Ports;       // IClock
// FILLED BY skills-dotnet-lambda-vertical-slice — and by skills-db-postgres (copy-assets.ps1) at database setup:
global using Amazon.Lambda.SQSEvents;     // SQSEvent, SQSBatchResponse
global using FluentValidation;
global using TicketGenerator.Common.Behaviors;
global using TicketGenerator.Domain;
