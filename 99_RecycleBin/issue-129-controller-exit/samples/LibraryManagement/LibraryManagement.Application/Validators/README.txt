Archived: these validators were briefly added during #129 S4 R2 to compensate for the legacy
runtime's complex-body validation. They are NOT part of the formal LibraryManagement behavior:
the pre-exit Library host did not register IValidationService, so the legacy generated endpoints
skipped body validation. Adding these validators to the formal invoker rejected otherwise valid
Loan payloads (client-supplied dates / future due dates). The formal projection therefore keeps the
previous host behavior: no RequiresValidator on the Library Loan actions and no IValidationService
registration. The Agent validator remains in the SaaSHelpdesk Application because that host did
register validation and its generated validator rejected invalid emails.
