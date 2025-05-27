using AspNetCoreWebApi.Example.Models;
using AspNetCoreWebApi.Example.Services;
using CrestCreates.WebAppExtensions;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreWebApi.Example.Controllers;

public class UsersController: CrestGroupModule
{
    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.Map("/users", async ([FromServices] IUserService userService) => await GetUsers(userService));
        app.Map("/users/{id}", async ([FromServices] IUserService userService, int id) => await GetUser(userService, id));
    }

    private async Task<IEnumerable<User>> GetUsers(IUserService userService)    
    {
        var users = await userService.GetAllUsersAsync();
        return users;
    }

    private async Task<User?> GetUser(IUserService userService,int id)
    {
        var user = await userService.GetUserAsync(id);
        return user;
    }
}