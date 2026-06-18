using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities
{
    [Entity(
        GenerateRepository = true,
        GenerateAuditing = true,
        OrmProvider = "SqlSugar",
        TableName = "TestCustomers"
    )]
    public class TestCustomer : FullyAuditedAggregateRoot<int>
    {
        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        public string Email { get; private set; } = string.Empty;
        public string PhoneNumber { get; private set; } = string.Empty;
        public DateTime BirthDate { get; private set; }
        public bool IsVip { get; private set; }

        public TestCustomer()
        {
            // ORM构造函数
        }

        public TestCustomer(int id, string firstName, string lastName, string email)
            : base()
        {
            Id = id;
            SetName(firstName, lastName);
            SetEmail(email);
        }

        public void SetName(string firstName, string lastName)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                throw new ArgumentException("名字不能为空", nameof(firstName));
            
            if (string.IsNullOrWhiteSpace(lastName))
                throw new ArgumentException("姓氏不能为空", nameof(lastName));

            FirstName = firstName.Trim();
            LastName = lastName.Trim();
        }

        public void SetEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("邮箱不能为空", nameof(email));
            
            // 简单的邮箱验证
            if (!email.Contains("@"))
                throw new ArgumentException("邮箱格式不正确", nameof(email));

            Email = email.Trim().ToLower();
        }

        public void SetPhoneNumber(string phoneNumber)
        {
            PhoneNumber = phoneNumber?.Trim() ?? string.Empty;
        }

        public void SetBirthDate(DateTime birthDate)
        {
            if (birthDate > DateTime.Now)
                throw new ArgumentException("出生日期不能是未来时间", nameof(birthDate));
            
            BirthDate = birthDate;
        }

        public void MakeVip()
        {
            IsVip = true;
        }

        public void RemoveVip()
        {
            IsVip = false;
        }

        public string GetFullName()
        {
            return $"{FirstName} {LastName}";
        }

        public int GetAge()
        {
            var today = DateTime.Today;
            var age = today.Year - BirthDate.Year;
            if (BirthDate.Date > today.AddYears(-age))
                age--;
            return age;
        }
    }
}
