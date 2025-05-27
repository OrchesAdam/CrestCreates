using AspNetCoreWebApi.Example.Models;

namespace AspNetCoreWebApi.Example.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<IEnumerable<User>> GetAllAsync();
    Task<User> CreateAsync(User user);
}