using AspNetCoreWebApi.Example.Models;
using AspNetCoreWebApi.Example.Repositories;
using CrestCreates.DependencyInjection;

namespace AspNetCoreWebApi.Example.Services;

[Scoped(typeof(IUserService))]
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    
    public UserService(IUserRepository userRepository, IEmailService emailService)
    {
        _userRepository = userRepository;
        _emailService = emailService;
    }
    
    public async Task<User?> GetUserAsync(int id)
    {
        return await _userRepository.GetByIdAsync(id);
    }
    
    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await _userRepository.GetAllAsync();
    }
    
    public async Task<User> CreateUserAsync(string name, string email)
    {
        var user = new User { Name = name, Email = email };
        var createdUser = await _userRepository.CreateAsync(user);
        
        // 发送欢迎邮件
        await _emailService.SendWelcomeEmailAsync(createdUser.Email, createdUser.Name);
        
        return createdUser;
    }
}