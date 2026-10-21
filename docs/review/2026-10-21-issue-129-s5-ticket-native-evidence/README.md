# #129 S5 Ticket NativeAOT evidence

Code head bound by every JSON `sha`: `eb8e480756e3a559698fd708aa5ccc6726b2e241` (the S5 fixture commit;
the later docs-only commit does not change code). RID: linux-x64.

- `ticket-native-http.json` — green native gate: `CRESTCREATES_TICKET_NATIVE_OK`, 15 scenario PASS, exit 0.
- `ticket-native-http-wrong-expectation.json` — mutation: `create:FAIL`, exit 1, no total sentinel.
- `ticket-native-http-wrong-validation.json` — mutation: `invalid-body-zero-business:FAIL`, exit 1, no total sentinel.

Each file records the publish command/exit/log, publish timeout flag, SHA, RID, gate exit/output, result and
failure fields. The publish is Release + `-p:CrestCreatesPublishMode=aot`, self-contained, linux-x64; the
executed binary is the published native artifact. Framework dependency trim/AOT warnings remain in the
publish log; the fixture's own projects emit no IL2026/IL3050 (asserted by the gate test).
