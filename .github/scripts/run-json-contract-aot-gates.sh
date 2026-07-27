#!/usr/bin/env bash

set -euo pipefail

# These test projects own publish, native link, execution of the original
# binary, scenario-marker assertions, and the final sentinel assertion.
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.Memory.AotFixture.Tests
