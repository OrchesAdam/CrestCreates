using CrestCreates.DependencyInjection;

namespace AspNetCoreWebApi.Example.Services;

[Singleton(typeof(IEmailService))]
public class EmailService : IEmailService
{
    public async Task SendWelcomeEmailAsync(string email, string name)
    {
        // 模拟发送邮件
        await Task.Delay(100);
        Console.WriteLine($"Sending welcome email to {email} for {name}");
    }
}