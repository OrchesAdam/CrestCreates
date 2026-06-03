# Spec: File Management

## 概述

工单附件上传、下载、预览功能。核心验证点是 File Management 模块的多 Provider 切换、Setting 驱动的文件限制、以及 FluentValidation 文件校验。

## 配置

```csharp
// 开发环境: 本地文件系统
services.AddFileManagement(options => {
    options.DefaultProvider = "Local";
    options.Providers["Local"] = new LocalFileSystemOptions
    {
        BasePath = "uploads/attachments",
        BaseUrl = "/files"
    };
});

// 可选: S3 Provider
services.AddFileManagement(options => {
    options.Providers["S3"] = new S3StorageOptions
    {
        Bucket = "helpdesk-attachments",
        Region = "us-east-1",
        AccessKey = "...",
        SecretKey = "..."
    };
});
```

## 存储路径规则

```
{tenantId}/{year}/{month}/{guid}.{ext}

示例: tenant-abc/2026/06/a1b2c3d4.pdf
```

## API

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/tickets/{ticketId}/attachments` | 上传附件到工单 |
| `POST` | `/api/tickets/{ticketId}/messages/{messageId}/attachments` | 上传附件到回复 |
| `GET` | `/api/attachments/{id}` | 获取附件元数据 |
| `GET` | `/api/attachments/{id}/download` | 下载附件 |
| `DELETE` | `/api/attachments/{id}` | 删除附件 |

### Upload Request (multipart/form-data)

```
POST /api/tickets/{ticketId}/attachments
Content-Type: multipart/form-data

file: screenshot.png
```

### Upload Response

```json
{
    "id": "guid",
    "fileName": "screenshot.png",
    "contentType": "image/png",
    "fileSize": 153600,
    "url": "/files/attachments/tenant-abc/2026/06/a1b2c3d4.png",
    "createdAt": "2026-06-01T10:30:00Z"
}
```

## 文件限制（Setting 驱动）

| Setting | 默认值 | 说明 |
|---------|--------|------|
| `Helpdesk.Attachment.MaxFileSizeMB` | 10 | 单文件最大体积 |
| `Helpdesk.Attachment.AllowedTypes` | jpg,jpeg,png,gif,pdf,doc,docx,xls,xlsx,txt,csv,zip | 允许的文件类型 |

### FluentValidation 校验

```csharp
public class UploadAttachmentDtoValidator : AbstractValidator<UploadAttachmentDto>
{
    public UploadAttachmentDtoValidator(ISettingProvider settingProvider)
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("文件不能为空")
            .Must((dto, file) =>
            {
                var maxSizeMB = settingProvider.GetOrNull<int>("Helpdesk.Attachment.MaxFileSizeMB");
                return file.Length <= maxSizeMB * 1024 * 1024;
            })
            .WithMessage((dto, file) =>
            {
                var maxMB = settingProvider.GetOrNull<int>("Helpdesk.Attachment.MaxFileSizeMB");
                return $"文件大小不能超过 {maxMB}MB";
            })
            .Must((dto, file) =>
            {
                var allowed = settingProvider.GetOrNull<string>("Helpdesk.Attachment.AllowedTypes");
                var ext = Path.GetExtension(file.FileName).TrimStart('.');
                return allowed.Split(',').Contains(ext, StringComparer.OrdinalIgnoreCase);
            })
            .WithMessage("不支持的文件类型");
    }
}
```

## Attachment Service 实现要点

```csharp
public class TicketAttachmentService
{
    private readonly IFileManagementService _fileService;
    private readonly IRepository<TicketAttachment, Guid> _attachmentRepo;
    private readonly IFileUrlService _urlService;

    public async Task<TicketAttachmentDto> UploadAsync(
        Guid ticketId, IFormFile file)
    {
        // 1. 计算 SHA256
        var hash = await ComputeHashAsync(file);

        // 2. 上传到文件服务
        using var stream = file.OpenReadStream();
        var storedFile = await _fileService.SaveAsync(new SaveFileInput
        {
            Stream = stream,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SubPath = $"{CurrentTenant.Id}/{DateTime.UtcNow:yyyy/MM}",
        });

        // 3. 创建 Attachment 实体
        var attachment = new TicketAttachment(
            ticketId,
            messageId: null,
            file.FileName,
            storedFile.FilePath,
            file.ContentType,
            file.Length,
            hash
        );

        await _attachmentRepo.InsertAsync(attachment);
        await unitOfWork.SaveChangesAsync();

        return attachment.ToDto(_urlService);
    }
}
```

## Feature 集成

附件功能受到存储容量 Feature 限制：

```
Feature: "Helpdesk.StorageLimitMB"
  默认值: 500 (MB)
  作用域: Tenant
  超限时: 上传返回 400 "存储空间已满，请联系管理员升级套餐"
```

```csharp
public async Task<TicketAttachmentDto> UploadAsync(...)
{
    // 检查存储配额
    var currentUsage = await GetStorageUsageAsync();
    var limit = await _featureChecker.GetAsync<int>("Helpdesk.StorageLimitMB");
    if (currentUsage + file.Length > limit * 1024 * 1024)
    {
        throw new StorageQuotaExceededException(limit, currentUsage);
    }
    // ... 继续上传
}
```

## 验证检查点

- [ ] 上传文件成功，返回 URL
- [ ] URL 可访问下载文件
- [ ] 超过 `MaxFileSizeMB` 的文件被拦截
- [ ] 不在 `AllowedTypes` 中的文件被拦截
- [ ] 修改 Setting 后限制即时生效（如改 MaxFileSizeMB 为 5，下次上传立即拦截 6MB 文件）
- [ ] 存储超过 Feature `StorageLimitMB` 后上传被拦截
- [ ] 删除附件后文件从存储中移除
- [ ] 同一文件重复上传生成两个 Attachment 记录（文件去重 V2）
- [ ] `SHA256` 哈希正确计算
