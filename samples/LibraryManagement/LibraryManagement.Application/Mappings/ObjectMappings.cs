using CrestCreates.Domain.Shared.ObjectMapping;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Mappings;

[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }

[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class UpdateBookDtoToBookMapper { }

[GenerateObjectMapping(typeof(Category), typeof(CategoryDto))]
public static partial class CategoryToCategoryDtoMapper { }

[GenerateObjectMapping(typeof(UpdateCategoryDto), typeof(Category), Direction = MapDirection.Apply)]
public static partial class UpdateCategoryDtoToCategoryMapper { }

[GenerateObjectMapping(typeof(Loan), typeof(LoanDto))]
public static partial class LoanToLoanDtoMapper { }

[GenerateObjectMapping(typeof(Member), typeof(MemberDto))]
public static partial class MemberToMemberDtoMapper { }
