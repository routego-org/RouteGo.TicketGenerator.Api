#!/bin/bash
# Ticket Generator's own queues, on top of the shared 01-resources.sh from the architecture document
# (which already creates the topics, the routego-tickets-local bucket and the helper functions).
# Differs from doc 00's catalog: two queues instead of ticket-q, one per capability.
set -e
export AWS_DEFAULT_REGION=ap-south-1

mkq () { # name visibility-timeout
  awslocal sqs create-queue --queue-name $1-dlq >/dev/null
  DLQ_ARN=$(awslocal sqs get-queue-attributes --queue-url http://localhost:4566/000000000000/$1-dlq --attribute-names QueueArn --query Attributes.QueueArn --output text)
  awslocal sqs create-queue --queue-name $1 --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"$DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\",\"ReceiveMessageWaitTimeSeconds\":\"20\",\"VisibilityTimeout\":\"$2\"}" >/dev/null
}

# 180 s = 6 x the 30 s function timeout.
mkq ticket-confirmed-q 180
mkq ticket-seats-changed-q 180

T_ARN=arn:aws:sns:ap-south-1:000000000000:routego-booking-events
awslocal sns subscribe --topic-arn $T_ARN --protocol sqs \
  --notification-endpoint arn:aws:sqs:ap-south-1:000000000000:ticket-confirmed-q \
  --attributes '{"RawMessageDelivery":"true","FilterPolicy":"{\"eventType\":[\"BookingConfirmed\"]}"}' >/dev/null
awslocal sns subscribe --topic-arn $T_ARN --protocol sqs \
  --notification-endpoint arn:aws:sqs:ap-south-1:000000000000:ticket-seats-changed-q \
  --attributes '{"RawMessageDelivery":"true","FilterPolicy":"{\"eventType\":[\"BookingSeatsChanged\"]}"}' >/dev/null
