#!/usr/bin/env bash

set -euo pipefail

dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests --no-build
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests --no-build
dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests --no-build
dotnet test tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests --no-build
