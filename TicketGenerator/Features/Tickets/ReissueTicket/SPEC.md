# CAP-TKT-002 — Re-issue ticket

- Capability: CAP-TKT-002
- Canonical spec: `spec-RouteGo.TicketGenerator` → `capabilities/tickets/features/e-ticket-issuing.md` (version 0.1.0) — the source of truth; this file only projects it
- Area: Tickets
- Slice: `Features/Tickets/ReissueTicket/`
- Function entry: `ReissueTicketFunction` (unique deploy unit)

## Contract

- Trigger / event source: `ticket-seats-changed-q` (SQS, DLQ `ticket-seats-changed-dlq`), fed by a filtered SNS subscription to `routego-booking-events` for `BookingSeatsChanged`. Wired in `template.yaml` (event source mapping with `ReportBatchItemFailures`, maximum concurrency 5).
- Input: a `BookingSeatsChanged` event in the RouteGo envelope: event id, time it occurred, correlation id and the ticket snapshot (booking, contact, operator, trip, seats with passengers and fares, total paid).
- Effect / output: Replaces the booking's stored e-ticket with one showing the new seats, marked `Re-issued after seat change`, with the event's occurred time as its change time, then publishes `TicketReady` (`issueKind` `Reissue`). A seat change that arrives before any ticket exists still makes the ticket, as a re-issue.
- Idempotency key: the booking id with the event's change time. The same change time makes the identical ticket again in the same place and announces it again (same `TicketReady` event id); an earlier change time is skipped and announces nothing.
- On failure: any failure (storage or the announcement channel unavailable, or unusable event data) is reported as a partial-batch failure for that one message, so only it is retried and the others are not held up. The queue's redrive policy parks it after 5 receives; on that last receive the function logs `TICKET_NOT_GENERATED` naming the booking and event, which the platform alarm channel picks up. A replay after the fix is a repeat delivery. The booking in Booking is never affected.

## Acceptance criteria

- [x] AC-6: A seat change re-issues the ticket with the new seats — `HandleAsync_SeatChange_ReplacesTheStoredTicketWithTheNewSeats`
- [x] AC-7: The re-issue is announced as a re-issue — `HandleAsync_SeatChange_AnnouncesAReissueWithItsChangeTime`
- [x] AC-8: A late confirmation never replaces a newer re-issue — covered in the IssueTicket slice (the late event is a BookingConfirmed)
- [x] AC-9: An older seat change never replaces a newer one — `HandleAsync_OlderSeatChange_KeepsTheNewerTicket`
- [x] AC-10: A seat change before any ticket makes the ticket — `HandleAsync_SeatChangeBeforeAnyTicket_MakesTheTicketAsAReissue`
- [x] AC-13: A re-issue that cannot be made leaves the current ticket — `HandleAsync_StorageRefusesTheReissue_ReportsFailureAndLeavesTheCurrentTicket`
- [x] Idempotency: the same seat change twice leaves one ticket — `HandleAsync_SameSeatChangeTwice_KeepsOneTicket`

## Invariants touched

- A ticket is made only from a complete snapshot (`TicketSnapshotValidator`, delegating to `TicketRules`).
- Newest booking change wins (`TicketRules.CompareChange`, enforced atomically by the store).
- Only a seat change causes a re-issue (`TicketIssueKinds.Reissue`).
