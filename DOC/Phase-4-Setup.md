# Phase 4: email, payments, and calendar setup

The Phase 4 API is workspace-scoped. SMTP settings and inbound email secrets are encrypted with ASP.NET Core Data Protection before they are stored. Stripe keys and Google OAuth application credentials are read from backend environment settings; refresh tokens are encrypted in SQL Server.

## 1. Apply the database migration

From `Backend/leadflow-crm.API`, run:

```powershell
dotnet ef database update --configuration Phase4
```

The migration adds email conversation/message tables, workspace integration status and event history, Stripe payment records, and Google Calendar connection and appointment tables. It also grants the new inbox and calendar permissions to existing workspace roles.

## 2. Configure workspace email

Sign in as a workspace Owner or Admin, open **Integrations**, and enter the SMTP host, port, sender address, TLS setting, and optional credentials. Use **Send test email** to verify delivery. The same sender delivers lead escalation notices and inbox replies.

For inbound email, enter a webhook secret on the Integrations page and configure an email service or relay to send the following normalized JSON to:

```text
POST /api/integrations/email/{workspaceId}/inbound
```

Required headers:

```text
X-Leadflow-Timestamp: <Unix timestamp in seconds>
X-Leadflow-Signature: <lowercase or uppercase hex HMAC-SHA256>
```

The signature is `HMACSHA256(secret, timestamp + "." + exactRawRequestBody)`. Requests older than five minutes are rejected. The JSON fields are `messageId`, `threadId`, `fromAddress`, `toAddress`, `subject`, and `textBody`; `inReplyTo`, `references`, and `receivedAt` are optional. Store and configure a real provider secret; do not put it in frontend code. Unknown senders enter the Phase 3 lead intake and assignment workflow.

The inbound endpoint is provider-neutral: the selected email provider or relay must support an authenticated inbound webhook and map its event into this payload and signature scheme. SMTP alone only handles outbound messages.

## 3. Configure Stripe test mode

Set these environment variables for the API process (PowerShell example):

```powershell
$env:Stripe__SecretKey = "sk_test_..."
$env:Stripe__WebhookSecret = "whsec_..."
$env:Stripe__SuccessUrl = "http://localhost:4200/?payment=success&session_id={CHECKOUT_SESSION_ID}"
$env:Stripe__CancelUrl = "http://localhost:4200/?payment=cancelled"
```

Only `sk_test_` keys are accepted. Configure a Stripe test webhook pointing to:

```text
POST https://<public-api-host>/api/payments/stripe/webhook
```

Subscribe to `checkout.session.completed`, `checkout.session.expired`, `checkout.session.async_payment_failed`, `checkout.session.async_payment_succeeded`, `payment_intent.succeeded`, `payment_intent.payment_failed`, and `charge.refunded`. The endpoint verifies Stripe's `Stripe-Signature` header against the exact raw body, applies a five-minute timestamp tolerance, records event IDs for duplicate protection, and checks amount/currency against the payment record. Stripe sends checkout in payment mode; the application does not store card data.

Payment links use the opportunity value and currency PKR. Stripe expects PKR amounts in minor units, so the API converts PKR with two decimal places into paise for the Checkout Session.

## 4. Configure Google Calendar OAuth

Create a Google OAuth Web application and enable the Google Calendar API. Add this authorized redirect URI exactly:

```text
https://<public-api-host>/api/integrations/calendar/callback
```

Set the backend environment values:

```powershell
$env:GoogleCalendar__ClientId = "<oauth-client-id>"
$env:GoogleCalendar__ClientSecret = "<oauth-client-secret>"
$env:GoogleCalendar__RedirectUri = "https://<public-api-host>/api/integrations/calendar/callback"
$env:Frontend__BaseUrl = "https://<frontend-host>"
```

Users with calendar permission connect their own Google account from **Calendar**. The app requests offline access and the event/free-busy scopes, encrypts each refresh token, checks free/busy before scheduling, and creates, updates, or deletes events. Appointment API timestamps are UTC; the UI accepts and displays `Asia/Karachi` time.

## 5. API routes

- `GET/PUT /api/integrations/email`, `POST /api/integrations/email/test`
- `GET /api/integrations/status`, `GET /api/integrations/history`
- `GET /api/email/conversations`, `GET /api/email/conversations/{id}`, `POST /api/email/conversations/{id}/reply`
- `POST /api/payments/checkout`, `GET /api/payments/opportunities/{opportunityId}`
- `POST /api/payments/stripe/webhook`
- `GET /api/integrations/calendar/status`, `POST /api/integrations/calendar/connect`, `DELETE /api/integrations/calendar/connection`
- `GET/POST /api/integrations/calendar/appointments`, `PUT/DELETE /api/integrations/calendar/appointments/{id}`

## Local development and key storage

Use user-scoped environment variables or a local secrets manager for provider credentials. Do not commit secrets to `appsettings*.json`. ASP.NET Core Data Protection must keep the same key ring between runs so encrypted SMTP passwords and Google refresh tokens remain readable. For hosted deployments, persist and protect that key ring according to the deployment platform.

Real provider delivery, webhook, OAuth, and payment tests require your SMTP credentials, an inbound email webhook provider, Stripe test credentials, and Google OAuth credentials. Without those values, the project can build and expose the integration endpoints, but the corresponding live provider flows cannot complete.
