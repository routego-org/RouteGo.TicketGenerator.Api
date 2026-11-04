# CAP-TKT-001 — Issue ticket

- Capability: CAP-TKT-001
- Canonical spec: `spec-RouteGo.TicketGenerator` → `capabilities/tickets/features/e-ticket-issuing.md` (version 0.1.0) — the source of truth; this file only projects it
- Area: Tickets
- Slice: `Features/Tickets/IssueTicket/`
- Function entry: `IssueTicketFunction` (unique deploy unit)

## Contract

- Trigger / event source: `ticket-confirmed-q` (SQS, DLQ `ticket-confirmed-dlq`), fed by a filtered SNS subscription to `routego-booking-events` for `BookingConfirmed`. Wired in `template.yaml` (event source mapping with `ReportBatchItemFailures`, maximum concurrency 5).
- Input: a `BookingConfirmed` event in the RouteGo envelope: event id, time it occurred, correlation id and the ticket snapshot (booking, contact, operator, trip, seats with passengers and fares, total paid).
- Effect / output: Stores the booking's single e-ticket (`tickets/<bookingId>.pdf`) as a first issue, with the event's occurred time as its change time, then publishes `TicketReady` (`issueKind` `FirstIssue`).
- Idempotency key: the booking id with the event's change time. The same change time makes the identical ticket again in the same place and announces it again (same `TicketReady` event id); an earlier change time is skipped and announces nothing.
- On failure: any failure (storage or the announcement channel unavailable, or unusable event data) is reported as a partial-batch failure for that one message, so only it is retried and the others are not held up. The queue's redrive policy parks it after 5 receives; on that last receive the function logs `TICKET_NOT_GENERATED` naming the booking and event, which the platform alarm channel picks up. A replay after the fix is a repeat delivery. The booking in Booking is never affected.

## Acceptance criteria

- [x] AC-1: A confirmed booking gets its first issue — `HandleAsync_ConfirmedBooking_StoresFirstIssueWithItsChangeTime`, and the printed content in `QuestPdfTicketRenderer.Tests.cs`
- [x] AC-2: The first issue is announced with what the email needs — `HandleAsync_ConfirmedBooking_AnnouncesFirstIssueWithWhatTheEmailNeeds`
- [x] AC-3: The QR code holds the booking ID only; no email, customer or payment id is printed — `Render_AnyTicket_NeverPrintsTheContactEmail` (the QR payload itself is not decoded by a test)
- [x] AC-4: An arrival on a later date shows that date — `Render_FirstIssue_PrintsTheBookingAndTripInIndianTimeAndRupees`, `Arrival_NextDayInIst_ShowsThatDate`
- [x] AC-5 / Idempotency: the same event twice leaves one unchanged ticket and announces again — `HandleAsync_SameEventTwice_KeepsOneTicketAndAnnouncesItAgain`
- [x] AC-8: A late confirmation never replaces a newer re-issue — `HandleAsync_NewerChangeAlreadyStored_SkipsTheLateConfirmation`
- [x] AC-11: Unusable event data (no seats) fails — `ValidateAsync_ConfirmedBookingWithNoSeats_Fails`
- [x] AC-12: One failing event does not hold up the others — `HandleAsync_OneBadRecordInTheBatch_ReportsOnlyThatRecord`
- [x] AC-14: A replay after the fix makes the ticket — `HandleAsync_ParkedEventReplayedAfterTheFix_MakesTheTicket`

## Invariants touched

- A ticket is made only from a complete snapshot (`TicketSnapshotValidator`, delegating to `TicketRules`).
- Newest booking change wins (`TicketRules.CompareChange`, enforced atomically by the store).
- Times print in IST and amounts in whole rupees with Indian grouping (`TicketFormat`).
