# Stripe and PayPal project invoices

This module collects **one full payment per construction-project invoice**. It is not SaaS subscription billing, a tax engine, a general ledger, or a marketplace onboarding system. CompanyAdmin issues invoices and shares a private client payment link. Clients do not need workspace accounts. Managers and engineers cannot administer invoices.

Supported invoice currencies are USD, EUR, GBP, CAD and AUD (two decimal places). The currency is selected explicitly. No exchange-rate conversion is performed, and project budgets are not changed. PayPal does not support PKR. Only use a provider account that is eligible for your business country and the selected currency.

## Before use

1. Apply migration `20261110064957_AddProjectInvoices` with the deployment database identity, after backup/review. It creates invoice/line tables; it does not modify existing project data. Projects with invoices cannot be deleted.
2. Retain the API Data Protection key ring. Payment links are encrypted at rest, their lookup values are SHA-256 hashes, and the original link must remain recoverable for stable checkout retries.
3. Set `Payments__WebBaseUrl` to the exact trusted HTTPS Web origin, e.g. `https://app.example.com`. Return URLs are built from this configuration, never a request Host header or customer input.
4. Configure separate merchant credentials **per company ID**, in protected environment settings or a secrets manager. There is no fallback to a shared platform merchant. Never commit secret keys or put them in Web settings/browser JavaScript. Merchant IDs/company mappings must be verified by the deployment administrator.

For company ID 12, set:

| Environment setting | Value |
| --- | --- |
| `Payments__Merchants__12__Stripe__Mode` | `Sandbox` first, `Live` only after acceptance tests |
| `Payments__Merchants__12__Stripe__SecretKey` | Stripe test/live secret key matching the mode |
| `Payments__Merchants__12__Stripe__WebhookSecret` | Signing secret for this endpoint and mode |
| `Payments__Merchants__12__PayPal__Mode` | `Sandbox` first, `Live` only after acceptance tests |
| `Payments__Merchants__12__PayPal__ClientId` | REST app client ID |
| `Payments__Merchants__12__PayPal__ClientSecret` | REST app secret |
| `Payments__Merchants__12__PayPal__MerchantId` | Receiving business merchant ID, from the same environment |
| `Payments__Merchants__12__PayPal__WebhookId` | Webhook ID registered for that REST app |

Invoices retain their sandbox/live environment. Do not switch mode to process an old invoice. Keep test data in staging and production data in a separate database. The UI identifies sandbox records and will not issue invoices for unconfigured providers. A connected indicator means configuration is present, **not that credentials have passed a live network test**.

## Provider setup

Register these public HTTPS API endpoints, replacing `12` with the company ID:

- Stripe: `https://api.example.com/api/payments/webhooks/stripe/12`. Subscribe to `checkout.session.completed`, `checkout.session.expired`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `charge.refunded`, `charge.dispute.created`, `charge.dispute.closed`. Use the API event format corresponding to `2026-10-28.endive`.
- PayPal: `https://api.example.com/api/payments/webhooks/paypal/12`. Subscribe to `CHECKOUT.ORDER.APPROVED`, `PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.PENDING`, `PAYMENT.CAPTURE.DECLINED`, `PAYMENT.CAPTURE.REFUNDED`, `PAYMENT.CAPTURE.REVERSED`.

Stripe uses hosted Checkout with dashboard-managed payment methods, server-side amounts and a stable idempotency key. Enable the intended methods in the merchant sandbox/live Dashboard. The current API rejects the legacy payment_method_types parameter; do not send it. Webhooks require an authentic raw-body HMAC and a timestamp within five minutes. PayPal uses OAuth, Orders v2 and server-side capture. Its webhook signature is verified through PayPal using the configured webhook ID. The server re-reads the payment and verifies invoice ID, amount and currency before changing status; PayPal also verifies the receiving merchant. Approval/redirect alone does not settle an invoice.

No client-supplied provider order ID is accepted for capture. A client returning from PayPal can click **Confirm payment** if the approval webhook has not arrived. Duplicate events do not duplicate the paid audit entry. Checkout URLs must belong to the provider's allowlisted HTTPS hosts.

## Operation and recovery

- From **Invoices & payments**, create an invoice with its line items and selected provider. Open its detail page and share the private link. Possession of the link grants access to that invoice; do not publish it or log query strings for `/Billing/Pay` and `/api/payments/invoice` in access logs/analytics.
- Invoice amount, client and currency are immutable. Only invoices with no checkout started can be voided. Issued records and payment audit entries are retained.
- A single provider is selected per invoice. This prevents two live Stripe/PayPal checkouts charging the same invoice through separate gateways.
- Repeated checkout requests reuse the existing session/order. Lost create responses retry the same idempotency key for at most five hours, within PayPal's default six-hour retention. After that, processing stops for manual reconciliation; it must never silently create a second order. Search the provider dashboard by invoice/reference before taking further action. This version has no automated recovery worker for this rare failure window.
- Expired/failed checkouts cannot be reopened in this version. First verify in the provider dashboard that no funds settled or remain pending; then issue a replacement invoice with a new number. Never duplicate an invoice merely because the client closed the browser.
- Use **Check provider status** to reconcile a payment. Refunds are initiated in the provider dashboard, not in this application. Full refunds become `Refunded`; partial refunds/reversals/disputes require `Review`. Webhook coverage is supplemented by manual reconciliation; automated dispute handling/accounting is not provided.
- Show totals by currency and environment if exporting to accounting. Do not sum sandbox and live records or different currencies.
- Watch provider webhook delivery failures and API 503/409 responses; maintain synchronized server clocks. The public API rate limiter is per remote IP; size it for the Web proxy's traffic before scaling. Configure trusted forwarded headers as described in README.

## Acceptance tests before live activation

The repository tests use isolated LocalDB and fake HTTP provider responses; no real merchant transaction is executed by them. Before live activation, use actual Stripe test mode and PayPal sandbox to verify success, cancel/decline, reload/double click, delayed/repeated webhook, full/partial refund, bad signature, unavailable provider, restart/key persistence, amount mismatch, and company isolation. Verify the correct recipient in each provider dashboard. Confirm HTTPS webhook delivery from the public internet and alerting on failed deliveries. Configure the business's invoice/tax/refund requirements separately.

References: [Stripe Checkout Sessions](https://docs.stripe.com/api/checkout/sessions/create), [Stripe webhook verification](https://docs.stripe.com/webhooks), [PayPal Orders v2](https://developer.paypal.com/api/orders/v2), [PayPal webhook verification](https://developer.paypal.com/api/rest/webhooks/rest/), [PayPal supported currencies](https://developer.paypal.com/api/codes/currency).
