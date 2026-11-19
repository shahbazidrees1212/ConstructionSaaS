# SaaS subscriptions: companies pay the platform owner

The CompanyAdmin buys workspace access from the ConstructionSaaS owner. Stripe recurring Checkout and PayPal Subscriptions charge the **platform merchant**, not a construction company's merchant. No client invoices are required. The old project-invoice records and routes remain for historical continuity, but the primary admin navigation now opens **Subscription & billing**.

## Activation

1. Apply the `AddCompanySubscriptions` migration. It adds CompanySubscriptions and SubscriptionPayments; existing projects, users and invoices are preserved.
2. Decide the actual monthly/yearly prices and currency. No commercial price or free trial is silently assumed. All published plans currently provide the full workspace; per-plan seat/storage quotas are not advertised or enforced.
3. In the **platform owner's** Stripe sandbox, create a product with a recurring monthly and/or yearly fixed Price. In PayPal sandbox, create an active billing Plan with one unlimited REGULAR cycle, interval count 1, no trial and no setup fee. Match the configured price/currency exactly.
4. Put the owner's credentials in API User Secrets locally and a secret manager in production. These settings are separate from `Payments:Merchants:<company>` used by the legacy invoice module. Never reuse those tenant settings implicitly.
5. Configure the exact trusted HTTPS Web origin and signed webhook endpoints. Start with a separate sandbox database. Example values below are illustrative only and must be replaced with the owner's chosen price and real provider IDs.
6. Set `Subscriptions:Enabled=true` only after migration, plan configuration and acceptance tests. This turns on both checkout and server-side access enforcement. While false, the UI explicitly says rollout is not active and existing companies retain workspace access.

Example API User Secrets (merge with existing JWT/database settings; do not commit secrets):

```json
{
  "Subscriptions": {
    "Enabled": false,
    "Mode": "Sandbox",
    "WebBaseUrl": "https://localhost:7192",
    "Stripe": {
      "SecretKey": "sk_test_REPLACE",
      "WebhookSecret": "whsec_REPLACE"
    },
    "PayPal": {
      "ClientId": "REPLACE",
      "ClientSecret": "REPLACE",
      "WebhookId": "REPLACE"
    },
    "Plans": {
      "monthly": {
        "Name": "Workspace Monthly",
        "Description": "Your construction workspace, billed monthly.",
        "Interval": "month",
        "Amount": 25.00,
        "Currency": "USD",
        "StripePriceId": "price_REPLACE",
        "PayPalPlanId": "P-REPLACE"
      },
      "yearly": {
        "Name": "Workspace Yearly",
        "Description": "Your construction workspace, billed yearly.",
        "Interval": "year",
        "Amount": 250.00,
        "Currency": "USD",
        "StripePriceId": "price_REPLACE",
        "PayPalPlanId": "P-REPLACE"
      }
    }
  }
}
```

Remove an unconfigured provider block and its plan IDs. The application checks the provider's actual recurring price before creating checkout. Amount and provider plan IDs are never accepted from the browser. Keep published plan IDs and prices immutable for subscribed companies; use a new plan key for a new offer.

Enable Stripe's customer portal in the same sandbox/live account for payment-method updates and invoice receipts. Keep subscription plan/quantity changes disabled in the portal: this release verifies one fixed recurring price per subscription. The **Manage payment method & receipts** button creates a short-lived portal session using the company's verified customer ID; the browser cannot choose another customer. PayPal subscribers manage their funding method in their PayPal account.

## Webhooks and renewal

- Stripe endpoint: `/api/subscriptions/webhooks/stripe` (no tenant ID). Subscribe to `checkout.session.completed`, `checkout.session.expired`, `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.paid`, `invoice.payment_failed`. Use snapshot events matching API `2026-10-28.endive`.
- PayPal endpoint: `/api/subscriptions/webhooks/paypal`. Subscribe to `BILLING.SUBSCRIPTION.CREATED`, `BILLING.SUBSCRIPTION.ACTIVATED`, `BILLING.SUBSCRIPTION.UPDATED`, `BILLING.SUBSCRIPTION.CANCELLED`, `BILLING.SUBSCRIPTION.EXPIRED`, `BILLING.SUBSCRIPTION.SUSPENDED`, `BILLING.SUBSCRIPTION.PAYMENT.FAILED`, `PAYMENT.SALE.COMPLETED`.
- Local Stripe listener (run the CLI on your machine, not browser Shell):

```powershell
stripe listen --events checkout.session.completed,checkout.session.expired,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.paid,invoice.payment_failed --forward-to https://localhost:59093/api/subscriptions/webhooks/stripe
```

Store this listener's signing secret in `Subscriptions:Stripe:WebhookSecret`. It is independent of the legacy project-invoice webhook secret. PayPal requires a public HTTPS test endpoint/tunnel registered against the sandbox REST app.

Provider events trigger a fresh API read; a redirect or unverified event never activates a company. A five-minute worker reconciles up to 25 records per batch, oldest verification first. The admin can also use **Check payment status**. Monitor logs/delivery failures; increase worker capacity for large deployments. If renewal confirmation is delayed beyond the paid period, access fails closed until refreshed.

## Access and cancellation

- All authenticated workspace API operations require a verified paid Active subscription when enabled. Billing recovery, authentication and account-security APIs remain available. The Web app redirects blocked users to the subscription page; hiding navigation is not the security boundary.
- Only CompanyAdmin can initiate or cancel a subscription and see its payment history. Managers/engineers see the company access notice and must contact their admin.
- Active without a verified paid-through date grants no access. Pending, PastDue, Suspended and expired paid periods grant no access. No unapproved grace period/free trial is enabled.
- Sandbox subscriptions cannot unlock production, even if accidentally configured there. Use Development/Testing for sandbox acceptance; use a separate database and live platform credentials in production.
- Stripe cancellation sets `cancel_at_period_end`; PayPal cancellation stops future billing immediately. Both retain already verified paid-through access. Cancellation is not a refund.
- No simultaneous second subscription/provider is allowed. Changing plan/provider is supported after cancellation and the paid period ending; mid-cycle upgrades, prorations, automatic taxes and discount codes are intentionally not offered.
- The latest verified recurring charge is recorded on each reconciliation. Webhook processing records renewals as they happen; for missed historical periods, provider dashboards are the accounting source of truth. A full historical receipt/PDF import is not provided.
- Refunds/disputes are managed in the provider dashboard and require the owner to review/cancel/suspend access as appropriate; a refund is not an automatic subscription cancellation.

## Lost-response recovery

An attempt ID is persisted before checkout creation. Concurrent requests cannot reserve two attempts for one company; repeat requests reuse the provider idempotency key and stored URL. If the create response is lost, retry the same checkout within five hours. After that, stop and reconcile in the provider dashboard; never clear the attempt or create a second subscription blindly. Existing records are preserved. Provider identity changes and configuration/environment mismatches fail closed.

## Platform owner

`PlatformAdmin` is a separately authorized owner role. Public signup and team-member forms **cannot** assign it. Provision an owner account/role using an authorized Identity administration process; do not grant this role to a customer. On login the owner sees `/Subscription/Platform`, listing company subscription states and offering provider reconciliation. No default owner password or public promotion endpoint is introduced.

## Verification before live

Run the isolated subscription checks:

```powershell
.\.tools\dotnet10\dotnet.exe run --project tests/AssignmentChecks -- --subscription-only
```

These tests create/delete a uniquely named LocalDB database and simulate provider HTTP responses. Real acceptance testing must verify owner-account receipts, first payment, return/refresh, duplicate clicks, renewals, failed payment, cancellation, paid-period expiry, forged/repeated webhooks, cross-company isolation and employee restrictions against Stripe sandbox and PayPal sandbox. Keep API and Web running and rebuild/restart after code changes.

References: [Stripe subscription Checkout](https://docs.stripe.com/billing/subscriptions/build-subscriptions), [Stripe subscription lifecycle](https://docs.stripe.com/billing/subscriptions/overview), [PayPal Subscriptions API](https://developer.paypal.com/api/subscriptions/v1).
