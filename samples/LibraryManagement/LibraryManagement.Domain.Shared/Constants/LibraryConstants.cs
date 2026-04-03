namespace LibraryManagement.Domain.Shared.Constants;

public static class LibraryConstants
{
    public const int MaxBookTitleLength = 200;
    public const int MaxAuthorNameLength = 100;
    public const int MaxIsbnLength = 13;
    public const int MaxCategoryNameLength = 50;
    public const int MaxMemberNameLength = 100;
    public const int MaxEmailLength = 256;
    public const int MaxPhoneLength = 20;
    
    public const int DefaultLoanDays = 14;
    public const int MaxLoanDays = 30;
    public const int MaxBooksPerMember = 5;
    
    public const decimal DefaultLateFeePerDay = 1.00m;
    public const decimal MaxLateFee = 50.00m;
}
