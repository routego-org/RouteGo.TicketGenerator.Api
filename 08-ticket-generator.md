# 08. Ticket Generator (Lambda #2)

| | |
|---|---|
| **Type** | AWS Lambda, triggered by SQS |
| **Build phase** | 8 |
| **Data** | No database. Output goes to the private S3 bucket `routego-tickets-<account>` |
| **Runs** | Serverless (no VPC, no EC2) |
| **Repo path** | `lambdas/ticket-generator` (SAM stack `routego-lambdas`, shared with the Webhook Receiver) |

---

## 1. BRD: why it exists
- A confirmed booking must produce a PDF e-ticket (FR-F1).
- Rendering a PDF is a CPU-heavy burst. At festival peaks, thousands of confirmations can arrive in minutes, then nothing for hours.
- Running that on the same machines as the booking flow would steal CPU from customers who are still booking (BO-4, BO-6).

**Business outcome:** tickets at any volume with no idle cost and no effect on booking performance.

## 2. PRD: what it offers
| Capability | Behavior |
|---|---|
| PDF ticket | One PDF per booking: booking id, QR code, passengers and seats, route, departure time, operator, boarding notes |
| Storage | Private object `tickets/<bookingId>.pdf` |
| Signal | Publishes `TicketReady` so Notification can email the ticket link |

Rules:
- Generating twice for one booking overwrites the same key (same result).
- The ticket link in email goes to the Portal ticket page, which verifies ownership and redirects to a short-lived presigned URL (5 minutes). The bucket is never public.

## 3. FRD: how it behaves

### 3.1 Trigger and processing
- Event source: SQS `ticket-q` (subscribed to `routego-booking-events` for `BookingConfirmed`).
- Batch size 10, `ReportBatchItemFailures` enabled, maximum concurrency set on the event source mapping (for example 5) to protect downstream limits.
- For each record:
  1. Parse the envelope; read the ticket snapshot from the event (**no calls to other services**; this is why `BookingConfirmed` carries everything).
  2. Render the PDF (for example with QuestPDF; check its license terms for your use).
  3. `PutObject` to `tickets/<bookingId>.pdf` with SSE-KMS.
  4. Publish `TicketReady {bookingId, s3Key}` to `routego-ticket-events`.
  5. On exception: add the record's message id to `batchItemFailures` so that only that message is retried.
- After 5 failed receives the message goes to `ticket-dlq`.

### 3.2 Function settings
| Setting | Value |
|---|---|
| Runtime | Newest managed .NET runtime available in your region (check the Lambda runtimes page; older .NET versions are retired on a published schedule) |
| Memory | 512 to 1024 MB (more memory gives more CPU; measure) |
| Timeout | 30 s |
| Queue visibility timeout | At least 6 × the function timeout (180 s) |
| Environment | `TICKET_BUCKET`, `TICKET_TOPIC_ARN` |
| Execution role | `s3:PutObject` on `tickets/*`; `sns:Publish` on `routego-ticket-events`; SQS receive and delete on `ticket-q`; KMS use; CloudWatch Logs |

### 3.3 SAM template (essentials)
```yaml
AWSTemplateFormatVersion: '2010-09-09'
Transform: AWS::Serverless-2016-10-31
Parameters:
  TicketQueueArn: { Type: String }
  TicketBucket:   { Type: String }
  TicketTopicArn: { Type: String }
Resources:
  TicketGeneratorFunction:
    Type: AWS::Serverless::Function
    Properties:
      CodeUri: lambdas/ticket-generator/
      Handler: TicketGenerator::TicketGenerator.Function::Handler
      Runtime: dotnet8        # replace with the current supported .NET runtime id
      MemorySize: 1024
      Timeout: 30
      Environment:
        Variables: { TICKET_BUCKET: !Ref TicketBucket, TICKET_TOPIC_ARN: !Ref TicketTopicArn }
      Policies:
        - S3WritePolicy: { BucketName: !Ref TicketBucket }
        - SNSPublishMessagePolicy: { TopicName: routego-ticket-events }
      Events:
        Booked:
          Type: SQS
          Properties:
            Queue: !Ref TicketQueueArn
            BatchSize: 10
            FunctionResponseTypes: [ReportBatchItemFailures]
            ScalingConfig: { MaximumConcurrency: 5 }
```
(Check the SAM docs for the exact policy template names and the runtime identifier when you build it.)

### 3.4 Event contract
- Consumes `BookingConfirmed` (ticket snapshot: see Booking doc).
- Publishes `TicketReady`.

## 4. Dependencies
**Depends on (sync):** none.
**Consumes:** `BookingConfirmed`.

**Depended on by:** Notification (`TicketReady` for the "your ticket is ready" email); Portal (ticket page downloads the object from S3).

## 5. AWS services used and why
| AWS service | Use | Without it |
|---|---|---|
| **Lambda** | Run PDF rendering on demand | PDF CPU load competes with booking on the EC2 instances |
| **SQS trigger (event source mapping)** | Poll and batch | You write and run your own polling loop |
| **Partial batch response** | Retry only failed records | One bad record causes the whole batch to repeat, so good tickets are rendered again |
| **Max concurrency** | Cap parallel executions | A spike could overwhelm S3 or SNS quotas |
| **S3 (private) + presigned URL** | Store tickets | Tickets on instance disk are lost; public bucket leaks personal data |
| **SNS** | `TicketReady` | Tight coupling to Notification |
| **SQS DLQ** | Poison messages | A bad booking record retries forever |
| **SAM + CloudFormation** | Define and deploy | Click-ops that cannot be repeated or reviewed |
| **KMS, IAM, CloudWatch** | Encryption, least privilege, logs and alarms | n/a |

**What students learn:** event source mappings, partial batch failure, concurrency limits, visibility timeout vs function timeout, Lambda vs container as a cost and scaling decision, and why events should carry enough data.

*If the teacher wants only one Lambda in the course, this can be changed to a container worker on `routego-core`; the contract stays the same.*

## 6. Before building: AWS setup
1. S3 bucket `routego-tickets-<account>`: block public access, SSE-KMS, a lifecycle rule to move objects to infrequent access after 90 days (optional).
2. Queue `ticket-q` with DLQ `ticket-dlq`; visibility timeout 180 s; subscription to `routego-booking-events` filter `BookingConfirmed`.
3. Topic `routego-ticket-events` (Phase 0).
4. Edge role: `s3:GetObject` on `routego-tickets-<account>/tickets/*` (so that Portal can presign).
5. SAM CLI installed; an S3 bucket for SAM artifacts (created by `sam deploy --guided`).
6. Alarms: `ticket-dlq` visible messages; Lambda `Errors` and `Throttles`.

## 7. Local development
1. Install SAM CLI and Docker.
2. Sample event: `events/booking-confirmed-sqs.json`, an SQS event wrapping a `BookingConfirmed` envelope (keep a copy generated from real Booking output).
3. Invoke without AWS:
   ```bash
   sam build
   sam local invoke TicketGeneratorFunction -e events/booking-confirmed-sqs.json --env-vars env.local.json
   ```
   `env.local.json` points `TICKET_BUCKET` to the LocalStack bucket and sets the endpoint URL for the function's AWS clients (host address from inside the container, for example `http://host.docker.internal:4566`; on Linux add `--add-host` or use the Docker network name).
4. Open the generated PDF from `awslocal s3 cp s3://routego-tickets-local/tickets/<id>.pdf .`
5. Test partial batch failure with an event containing one valid and one malformed record: the response lists only the bad `itemIdentifier`.
6. Lambda event-source mapping against LocalStack depends on your LocalStack version and edition; if unavailable, trigger with `sam local invoke` manually.

## 8. Deploying to AWS
1. Prerequisites above.
2. `sam build && sam deploy --guided` (stack `routego-lambdas`, parameters from the template; allow IAM role creation).
3. Check the function and event source mapping in the console (state Enabled, batch size 10, max concurrency 5).
4. Edge role permission for presigning is in place; Portal ticket page implemented (file 11).
5. Verify:
   - Confirm a booking: a PDF appears in S3 within seconds; `TicketReady` is published; Notification emails the link.
   - Open the link: Portal redirects to a presigned URL; the URL stops working after 5 minutes.
   - CloudWatch log group `/aws/lambda/<function>` has the invocation.
6. **Break it on purpose:**
   - Remove `s3:PutObject` from the role: messages retry, then land in `ticket-dlq`; alarm fires; fix and **redrive**.
   - Send 2,000 `BookingConfirmed` messages (script) and watch concurrency scale and then return to zero, while booking latency stays unchanged.
   - Lower max concurrency to 1 to show queue age rising.
   - Set queue visibility timeout below the function timeout to see duplicate processing; then fix it.

## 9. Lab checklist
- [ ] One bad record in a batch does not repeat the good ones.
- [ ] Booking latency unchanged during the 2,000-message burst.
- [ ] Bucket has no public access; link expires.
