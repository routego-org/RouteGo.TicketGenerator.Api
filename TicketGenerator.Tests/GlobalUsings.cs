// Slice test files (linked in from {Project}/Features/**/*.Tests.cs) carry no using directives —
// this file holds the test project's shared set. The sibling slice skill EXTENDS it as its
// test helpers need more (e.g. Testcontainers, AngleSharp).
global using System.Text.Json;
global using Amazon.Lambda.SQSEvents;
global using Amazon.Lambda.TestUtilities;
global using FluentValidation;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Xunit;
global using TicketGenerator.Common;             // HttpClients, WireJson
global using TicketGenerator.Common.Behaviors;
global using TicketGenerator.Common.Ports;
global using TicketGenerator.Domain;
global using TicketGenerator.Infrastructure;
global using TicketGenerator.Tests.Support;
