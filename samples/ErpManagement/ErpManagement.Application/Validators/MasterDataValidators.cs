using ErpManagement.Application.Contracts.DTOs;
using FluentValidation;

namespace ErpManagement.Application.Validators;

public class CreateMaterialDtoValidator : AbstractValidator<CreateMaterialDto>
{
    public CreateMaterialDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("物料编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("物料名称必填且不超过200字符");
        RuleFor(x => x.BaseUnitId).NotEmpty().WithMessage("必须指定基本计量单位");
        RuleFor(x => x.SafetyStock).GreaterThanOrEqualTo(0).WithMessage("安全库存不能为负");
        RuleFor(x => x.LeadTimeDays).GreaterThanOrEqualTo(0).WithMessage("采购提前期不能为负");
    }
}

public class UpdateMaterialDtoValidator : AbstractValidator<UpdateMaterialDto>
{
    public UpdateMaterialDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("物料编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("物料名称必填且不超过200字符");
        RuleFor(x => x.BaseUnitId).NotEmpty().WithMessage("必须指定基本计量单位");
        RuleFor(x => x.SafetyStock).GreaterThanOrEqualTo(0).WithMessage("安全库存不能为负");
        RuleFor(x => x.LeadTimeDays).GreaterThanOrEqualTo(0).WithMessage("采购提前期不能为负");
    }
}

public class CreateMaterialCategoryDtoValidator : AbstractValidator<CreateMaterialCategoryDto>
{
    public CreateMaterialCategoryDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("分类编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("分类名称必填且不超过100字符");
    }
}

public class UpdateMaterialCategoryDtoValidator : AbstractValidator<UpdateMaterialCategoryDto>
{
    public UpdateMaterialCategoryDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("分类编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("分类名称必填且不超过100字符");
    }
}

public class CreateUnitOfMeasureDtoValidator : AbstractValidator<CreateUnitOfMeasureDto>
{
    public CreateUnitOfMeasureDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).WithMessage("单位编码必填且不超过32字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("单位名称必填且不超过100字符");
        RuleFor(x => x.Precision).InclusiveBetween(0, 6).WithMessage("精度需在0-6之间");
    }
}

public class UpdateUnitOfMeasureDtoValidator : AbstractValidator<UpdateUnitOfMeasureDto>
{
    public UpdateUnitOfMeasureDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).WithMessage("单位编码必填且不超过32字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("单位名称必填且不超过100字符");
        RuleFor(x => x.Precision).InclusiveBetween(0, 6).WithMessage("精度需在0-6之间");
    }
}

public class CreateWarehouseDtoValidator : AbstractValidator<CreateWarehouseDto>
{
    public CreateWarehouseDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("仓库编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("仓库名称必填且不超过100字符");
    }
}

public class UpdateWarehouseDtoValidator : AbstractValidator<UpdateWarehouseDto>
{
    public UpdateWarehouseDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("仓库编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("仓库名称必填且不超过100字符");
    }
}

public class CreateStorageLocationDtoValidator : AbstractValidator<CreateStorageLocationDto>
{
    public CreateStorageLocationDtoValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("必须指定所属仓库");
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("库位编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("库位名称必填且不超过100字符");
    }
}

public class UpdateStorageLocationDtoValidator : AbstractValidator<UpdateStorageLocationDto>
{
    public UpdateStorageLocationDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("库位编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("库位名称必填且不超过100字符");
    }
}

public class CreateSupplierDtoValidator : AbstractValidator<CreateSupplierDto>
{
    public CreateSupplierDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("供应商编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("供应商名称必填且不超过200字符");
        RuleFor(x => x.PaymentTermsDays).GreaterThanOrEqualTo(0).WithMessage("付款条件天数不能为负");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("邮箱格式不正确");
    }
}

public class UpdateSupplierDtoValidator : AbstractValidator<UpdateSupplierDto>
{
    public UpdateSupplierDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("供应商编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("供应商名称必填且不超过200字符");
        RuleFor(x => x.PaymentTermsDays).GreaterThanOrEqualTo(0).WithMessage("付款条件天数不能为负");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("邮箱格式不正确");
    }
}

public class CreateCustomerDtoValidator : AbstractValidator<CreateCustomerDto>
{
    public CreateCustomerDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("客户编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("客户名称必填且不超过200字符");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).WithMessage("信用额度不能为负");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("邮箱格式不正确");
    }
}

public class UpdateCustomerDtoValidator : AbstractValidator<UpdateCustomerDto>
{
    public UpdateCustomerDtoValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("客户编码必填且不超过64字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("客户名称必填且不超过200字符");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).WithMessage("信用额度不能为负");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("邮箱格式不正确");
    }
}

public class CreateBomDtoValidator : AbstractValidator<CreateBomDto>
{
    public CreateBomDtoValidator()
    {
        RuleFor(x => x.MaterialId).NotEmpty().WithMessage("必须指定父项物料");
        RuleFor(x => x.Version).NotEmpty().MaximumLength(32).WithMessage("BOM 版本必填且不超过32字符");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ComponentMaterialId).NotEmpty().WithMessage("组件物料不能为空");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("组件用量必须为正数");
        });
    }
}

public class UpdateBomDtoValidator : AbstractValidator<UpdateBomDto>
{
    public UpdateBomDtoValidator()
    {
        RuleFor(x => x.Version).NotEmpty().MaximumLength(32).WithMessage("BOM 版本必填且不超过32字符");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ComponentMaterialId).NotEmpty().WithMessage("组件物料不能为空");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("组件用量必须为正数");
        });
    }
}