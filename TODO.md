# BIMA TODO

## Deferred

- [ ] Register a production email provider adapter for `IEmailSender` (SMTP, SES, SendGrid, or Microsoft Graph).
- [ ] Remove development password-reset token exposure from HTTP responses before production deployment.
- [ ] Add integration tests for email delivery failures and reset-token delivery.

## Next Product Work

- [x] Complete claim status transitions, approvals, and reserve changes.
- [x] Add claim attachment and detail UI foundation.
- [ ] Add claim adjusters and settlement records.
- [x] Add billing invoice foundation and payment allocation.
- [ ] Add payment-provider integration.
- [ ] Add production identity/session monitoring and operational dashboards.
- [x] Add optional policy/customer foreign-key relationship.
- [ ] Backfill existing policies and enforce required customer relationships.
- [ ] Add broader unit, integration, and frontend test coverage.
- [ ] Add production logging, tracing, metrics, and deployment hardening.
