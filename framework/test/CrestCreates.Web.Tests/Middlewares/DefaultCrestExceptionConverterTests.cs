using CrestCreates.AspNetCore.Errors;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Localization.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AuthorizationPermissionException = CrestCreates.Authorization.Abstractions.CrestPermissionException;

namespace CrestCreates.Web.Tests.Middlewares;

public class DefaultCrestExceptionConverterTests
{
    [Fact]
    public void Convert_WithBusinessException_UsesExceptionErrorCode()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-business" };
        var exception = new CrestBusinessException("Tenant.AlreadyExists", "Tenant already exists.", "tenant-name");

        var result = converter.Convert(context, exception);

        result.Response.Code.Should().Be("Tenant.AlreadyExists");
        result.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.Response.Message.Should().Be("Tenant already exists.");
        result.Response.Details.Should().Be("tenant-name");
        result.Response.TraceId.Should().Be("trace-business");
    }

    [Fact]
    public void Convert_WithConcurrencyException_Returns409()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-409" };

        var result = converter.Convert(context, new CrestConcurrencyException("Book", "b1"));

        result.Response.Code.Should().Be("Crest.Concurrency.Conflict");
        result.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        result.Response.TraceId.Should().Be("trace-409");
        result.LogLevel.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Convert_WithPreconditionException_Returns428()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext();

        var result = converter.Convert(context, new CrestPreconditionRequiredException("Book", "b1"));

        result.Response.Code.Should().Be("Crest.Concurrency.PreconditionRequired");
        result.Response.StatusCode.Should().Be(428);
    }

    [Fact]
    public void Convert_WithPermissionException_Returns403()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext();

        var result = converter.Convert(context, new AuthorizationPermissionException("Books.Delete"));

        result.Response.Code.Should().Be("Crest.Auth.Forbidden");
        result.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Response.Details.Should().Be("Books.Delete");
    }

    [Fact]
    public void Convert_WithUnhandledException_HidesRawMessage()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-500" };

        var result = converter.Convert(context, new InvalidCastException("password=secret; stack detail"));

        result.Response.Code.Should().Be("Crest.InternalError");
        result.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        result.Response.Message.Should().Be("服务器内部错误。");
        result.Response.Details.Should().BeNull();
        result.Response.TraceId.Should().Be("trace-500");
        result.LogLevel.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void Convert_WithLocalizedMessage_UsesLocalization()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService>(new FakeLocalizationService(new Dictionary<string, string>
            {
                ["Crest.Concurrency.Conflict"] = "本地化并发冲突"
            }))
            .BuildServiceProvider();
        var converter = new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);

        var result = converter.Convert(new DefaultHttpContext(), new CrestConcurrencyException("Book", "b1"));

        result.Response.Message.Should().Be("本地化并发冲突");
    }

    private static DefaultCrestExceptionConverter CreateConverter()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService>(new FakeLocalizationService(new Dictionary<string, string>()))
            .BuildServiceProvider();
        return new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public FakeLocalizationService(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public string CurrentCulture => "zh-CN";

        public string GetString(string key) => _values.TryGetValue(key, out var value) ? value : key;

        public string GetString(string key, params object[] arguments) => string.Format(GetString(key), arguments);

        public string GetString(string key, string cultureName) => GetString(key);

        public string GetString(string key, string cultureName, params object[] arguments) => GetString(key, arguments);

        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(GetString(key));

        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(GetString(key, arguments));

        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(GetString(key));

        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(GetString(key, arguments));

        public IDisposable ChangeCulture(string cultureName) => new NoopDisposable();

        public Task<IDisposable> ChangeCultureAsync(string cultureName) => Task.FromResult<IDisposable>(new NoopDisposable());

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
