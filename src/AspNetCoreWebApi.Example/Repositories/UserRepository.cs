using AspNetCoreWebApi.Example.Models;
using CrestCreates.DependencyInjection;

namespace AspNetCoreWebApi.Example.Repositories;

[Scoped(typeof(IUserRepository))]
public class UserRepository : IUserRepository
{
    private static readonly List<User> _users = new()
    {
        new User { Id = 1, Name = "John Doe", Email = "john@example.com" },
        new User { Id = 2, Name = "Jane Smith", Email = "jane@example.com" }
    };

    public Task<User?> GetByIdAsync(int id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(user);
    }

    public Task<IEnumerable<User>> GetAllAsync()
    {
        return Task.FromResult<IEnumerable<User>>(_users);
    }

    public Task<User> CreateAsync(User user)
    {
        user.Id = _users.Max(u => u.Id) + 1;
        _users.Add(user);
        return Task.FromResult(user);
    }
}