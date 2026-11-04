# RouteGo.TicketGenerator

Turns every confirmed booking into a private PDF e-ticket and announces `TicketReady`. It is an
event-driven AWS Lambda worker (no API, no screen, no database) — see the product specs
`spec-RouteGo.TicketGenerator` and the system documents `00-overall-architecture.md` and
`08-ticket-generator.md`.

| Slice | Capability | Trigger | Effect |
|---|---|---|---|
| `Features/Tickets/IssueTicket` | CAP-TKT-001 | SQS `ticket-confirmed-q` (`BookingConfirmed`) | Stores the first issue, publishes `TicketReady` |
| `Features/Tickets/ReissueTicket` | CAP-TKT-002 | SQS `ticket-seats-changed-q` (`BookingSeatsChanged`) | Replaces the ticket with the new seats, publishes `TicketReady` |

Stack: .NET 10 (managed `dotnet10` Lambda runtime, arm64), `Amazon.Lambda.Annotations`, QuestPDF
(Community licence — confirm RouteGo qualifies), QRCoder, S3 (private, conditional writes), SNS.

## Build and test

```bash
git submodule update --init --recursive
dotnet build && dotnet test
python .agents/skills/skills-aspnetcore-project-setup/scripts/check_setup.py --root TicketGenerator --stack lambda
python .agents/skills/skills-dotnet-lambda-vertical-slice/scripts/check_structure.py --root TicketGenerator
```

## Configuration

Environment variables only (named once in `Infrastructure/ConfigKeys.cs`): `TICKET_BUCKET`,
`TICKET_TOPIC_ARN`. Locally they are seeded for `DOTNET_ENVIRONMENT=Development`. The function holds
no secrets; it reaches S3 and SNS through its execution role.

## Deploy

`infra/template.yaml` is the SAM template (two functions, queue triggers with partial batch
response and concurrency 5, least-privilege access, `TICKET_NOT_GENERATED` metric and alarm). The
queues, bucket, topics and KMS key are platform prerequisites (doc 08 section 6); for LocalStack,
`infra/local/localstack-init/02-ticket-generator.sh` adds this service's two queues.

The ticket's change time is kept in S3 object metadata and written with conditional requests
(`If-None-Match` to create, `If-Match` on the ETag to replace), so two events for one booking handled
at once cannot let an older ticket win. LocalStack must support S3 conditional writes for local runs.
