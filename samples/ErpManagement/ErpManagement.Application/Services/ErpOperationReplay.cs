using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Domain.Shared.Exceptions;
using ErpManagement.Application.Contracts.DTOs;

namespace ErpManagement.Application.Services;

internal sealed class ErpOperationConflictException() : CrestException(
    "ERP_OPERATION_ID_CONFLICT", 409, "该操作 ID 已用于不同的请求。", "如需提交新操作，请使用新的 operationId。");

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkOrderDto))]
[JsonSerializable(typeof(ArApLedgerDto))]
internal sealed partial class ErpOperationReplayJsonContext : JsonSerializerContext;
