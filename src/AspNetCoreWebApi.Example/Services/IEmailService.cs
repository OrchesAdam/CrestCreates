namespace AspNetCoreWebApi.Example.Services;

public interface IEmailService
{
    Task SendWelcomeEmailAsync(string email, string name);
}