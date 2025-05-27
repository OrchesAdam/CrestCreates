using AspNetCoreWebApi.Example.Models;

namespace AspNetCoreWebApi.Example.Services;

public interface IUserService
{
    Task<User?> GetUserAsync(int id);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<User> CreateUserAsync(string name, string email);
}