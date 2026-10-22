# #129 S5 Ticket NativeAOT evidence

Code head bound by every JSON `sha`: `5e3878b2f8efd349480099fa401c321faa3c875b` (S5 R1 gate
strengthening commit; later docs/evidence and memory commits do not change code). RID: linux-x64.

Merged: PR #152, merge commit `2f4c54024d8e2e9c5b8aecad1a399669dadb28a6` (2026-10-22; branch head
`13ad93ef`). These evidence files were not rewritten by the merge or post-merge cleanup.

- `ticket-native-http.json` — green native gate: `CRESTCREATES_TICKET_NATIVE_OK`, 15 scenario PASS, exit 0.
- `ticket-native-http-wrong-expectation.json` — `create:FAIL`, exit 1, no total sentinel.
- `ticket-native-http-wrong-validation.json` — `invalid-body-zero-business:FAIL` (status expectation), exit 1, no sentinel.
- `ticket-native-http-late-validation.json` — validation disabled with the real business/UoW/repository reached;
  `invalid-body-zero-business:FAIL: invalid input must not enter the real business service`, exit 1, no sentinel.
- `ticket-native-http-decoy-metadata.json` — metadata probe replaced by a decoy contribution with no Ticket endpoints;
  `metadata-rejection:FAIL: the metadata probe must contain the Ticket contribution`, exit 1, no sentinel.

Each file records the publish command/exit/log, publish timeout flag, SHA, RID, gate exit/output, result and
failure fields. The publish is Release + `-p:CrestCreatesPublishMode=aot`, self-contained, linux-x64; the
executed binary is the published native artifact. Framework dependency trim/AOT warnings remain in the
publish log; the fixture's own projects emit no IL2026/IL3050 (asserted by the gate test).

The green negative metadata scenario locks the Ticket owner, 11 endpoint/capability/identity entries and the
`CreateTicketDto/CreateAsync/Body` plus `DynamicApiResponse<TicketDto>/CreateAsync/ResultEnvelope` roots before
clearing JSON; the exception must name that owner/action/type and leave zero routes.
