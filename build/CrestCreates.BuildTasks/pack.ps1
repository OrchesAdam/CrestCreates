# CrestCreates.BuildTasks NuGet 打包脚本

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ".\artifacts"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CrestCreates.BuildTasks NuGet Packager" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 确保输出目录存在
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    Write-Host "Created output directory: $OutputDirectory" -ForegroundColor Green
}

# 清理之前的构建
Write-Host "Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path "bin\$Configuration") {
    Remove-Item -Recurse -Force "bin\$Configuration"
}
if (Test-Path "obj") {
    Remove-Item -Recurse -Force "obj"
}

# 构建项目
Write-Host "Building project ($Configuration)..." -ForegroundColor Yellow
$buildResult = dotnet build -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green

# 更新 nuspec 版本
$nuspecPath = ".\CrestCreates.BuildTasks.nuspec"
[xml]$nuspec = Get-Content $nuspecPath
$nuspec.package.metadata.version = $Version
$nuspec.Save($nuspecPath)
Write-Host "Updated nuspec version to: $Version" -ForegroundColor Green

# 打包 NuGet
Write-Host "Creating NuGet package..." -ForegroundColor Yellow
$packResult = nuget pack $nuspecPath -OutputDirectory $OutputDirectory -NoDefaultExcludes

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pack failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Pack successful!" -ForegroundColor Green

# 显示结果
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Package created successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package location: $OutputDirectory\CrestCreates.BuildTasks.$Version.nupkg" -ForegroundColor Yellow
Write-Host ""
Write-Host "To publish to NuGet.org:" -ForegroundColor Cyan
Write-Host "  dotnet nuget push $OutputDirectory\CrestCreates.BuildTasks.$Version.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json" -ForegroundColor Gray
Write-Host ""
Write-Host "To publish to local feed:" -ForegroundColor Cyan
Write-Host "  dotnet nuget add source \"C:\LocalNuGet\" --name LocalFeed" -ForegroundColor Gray
Write-Host "  dotnet nuget push $OutputDirectory\CrestCreates.BuildTasks.$Version.nupkg --source LocalFeed" -ForegroundColor Gray
Write-Host ""
