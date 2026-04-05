namespace CrestCreates.Aop.Abstractions.Options;

public class InterceptorOrderOptions
{
    public int ExceptionHandling { get; set; } = -1000;
    public int UnitOfWork { get; set; } = -500;
    public int Permission { get; set; } = -400;
    public int MultiTenant { get; set; } = -300;
    public int DataPermission { get; set; } = -200;
    public int Cache { get; set; } = -100;
    public int Audit { get; set; } = 0;
}
