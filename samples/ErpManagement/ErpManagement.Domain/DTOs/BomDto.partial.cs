namespace ErpManagement.Application.Contracts.DTOs;

// Partial extensions for the [Entity]-generated BOM DTOs. The generator emits flat
// header DTOs into the Domain assembly; line payloads are added here, following the
// CategoryDto.partial precedent. Hand-writing same-named DTOs in another assembly
// collides with the generated types (CS0436) and splits validator symbol binding.

public partial class BomDto
{
    public List<BomLineDto> Lines { get; set; } = new();
}

public partial class CreateBomDto
{
    public List<BomLineInput> Lines { get; set; } = new();
}

public partial class UpdateBomDto
{
    public List<BomLineInput> Lines { get; set; } = new();
}

public class BomLineInput
{
    public Guid ComponentMaterialId { get; set; }
    public decimal Quantity { get; set; }
    public int Position { get; set; }
}