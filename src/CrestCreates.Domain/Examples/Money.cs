using System;
using System.Collections.Generic;
using CrestCreates.Domain.ValueObjects;

namespace CrestCreates.Domain.Examples
{
    public class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }
        
        public Money(decimal amount, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
                throw new ArgumentException("货币代码不能为空", nameof(currency));
                
            Amount = amount;
            Currency = currency;
        }
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
        
        public Money Add(Money money)
        {
            if (Currency != money.Currency)
                throw new InvalidOperationException("不能添加不同货币的金额");
                
            return new Money(Amount + money.Amount, Currency);
        }
        
        public Money Subtract(Money money)
        {
            if (Currency != money.Currency)
                throw new InvalidOperationException("不能减去不同货币的金额");
                
            return new Money(Amount - money.Amount, Currency);
        }
        
        public Money Multiply(decimal multiplier)
        {
            return new Money(Amount * multiplier, Currency);
        }
        
        public override string ToString()
        {
            return $"{Amount} {Currency}";
        }
        
        public static Money FromCNY(decimal amount)
        {
            return new Money(amount, "CNY");
        }
        
        public static Money FromUSD(decimal amount)
        {
            return new Money(amount, "USD");
        }
    }
}
