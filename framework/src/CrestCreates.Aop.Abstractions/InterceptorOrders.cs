namespace CrestCreates.Aop.Abstractions;

public static class InterceptorOrders
{
    public const int ExceptionHandling = -1000;
    public const int UnitOfWork = -500;
    public const int Permission = -400;
    public const int MultiTenant = -300;
    public const int DataPermission = -200;
    public const int Cache = -100;
    public const int Audit = 0;
    public const int Logging = 100;
}
