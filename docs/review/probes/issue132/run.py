#!/usr/bin/env python3
"""Build and execute the gap probe against a preserved exact-source Git archive."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

BASELINE = "5f4cfc8f37d6ef0e996598eb08ae0922b8b60a34"
parser = argparse.ArgumentParser()
parser.add_argument("--source-ref", default=BASELINE)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
probe = Path(__file__).resolve().parent
repo = probe.parents[3]
sha = subprocess.check_output(["git", "rev-parse", args.source_ref + "^{commit}"], cwd=repo, text=True).strip()
snapshot = Path(tempfile.mkdtemp(prefix="crest-issue132-"))
archive = subprocess.check_output(["git", "archive", sha], cwd=repo)
with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
    tar.extractall(snapshot, filter="data")
target = snapshot / "docs/review/probes/issue132"
target.mkdir(parents=True, exist_ok=True)
for name in ("Program.cs", "AuditRetentionGapProbe.csproj", "run.py"):
    shutil.copyfile(probe / name, target / name)
project = target / "AuditRetentionGapProbe.csproj"
log = snapshot / "gap-probe-build.log"
with log.open("w") as stream:
    for command in (
        ["dotnet", "restore", str(project), "-m:1", "-p:NuGetAudit=false", "-v:minimal"],
        ["dotnet", "build", str(project), "--no-restore", "-m:1", "-v:minimal"],
    ):
        result = subprocess.run(command, cwd=snapshot, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode:
            raise SystemExit(f"Build failed ({result.returncode}); snapshot and log retained: {log}")
completed = subprocess.run(
    ["dotnet", str(target / "bin/Debug/net10.0/AuditRetentionGapProbe.dll")],
    cwd=snapshot, text=True, capture_output=True,
)
(snapshot / "gap-probe-stdout.json").write_text(completed.stdout)
(snapshot / "gap-probe-stderr.txt").write_text(completed.stderr)
if completed.returncode:
    raise SystemExit(f"Probe failed ({completed.returncode}); outputs retained: {snapshot}")
observations = json.loads(completed.stdout)
if observations.get("sentinel") != "CRESTCREATES_ISSUE132_GAPS_REPRODUCED" or len(observations["cases"]) != 10:
    raise SystemExit("Missing sentinel or incomplete case matrix")
paths = [
    "src/Framework/Ddd/CrestCreates.Application/AuditLog/AuditLogCleanupAppService.cs",
    "src/Framework/Ddd/CrestCreates.Domain/Repositories/IAuditLogRepository.cs",
    "src/Framework/Ddd/CrestCreates.Domain/AuditLog/AuditLog.cs",
    "src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreAuditLogRepository.cs",
    "src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepositoryBase.cs",
    "src/Persistence/CrestCreates.Data.EFCore/DbContexts/EfCoreDbContextAdapter.cs",
    "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlAuditSink.cs",
    "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeMigrationRunner.cs",
]
evidence = {
    "source_commit": sha,
    "sdk": subprocess.check_output(["dotnet", "--version"], cwd=snapshot, text=True).strip(),
    "execution": "JIT; real EF SQLite cleanup; mutable tenant and fixed settings fixture leaves; no HTTP/auth/PG/native execution",
    "snapshot": str(snapshot),
    "source_sha256": {p: hashlib.sha256((snapshot / p).read_bytes()).hexdigest() for p in paths},
    "probe_sha256": {p: hashlib.sha256((target / p).read_bytes()).hexdigest() for p in ("Program.cs", "AuditRetentionGapProbe.csproj", "run.py")},
    **observations,
}
args.output.resolve().parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n")
print(f"10/10 baseline observations matched; evidence: {args.output.resolve()}; retained snapshot: {snapshot}")
