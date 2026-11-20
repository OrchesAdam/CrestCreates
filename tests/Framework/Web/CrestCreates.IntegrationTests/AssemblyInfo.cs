using Xunit;

// ERP 宿主夹具在进程内共享静态状态（模块初始化、权限/Feature 缓存），
// 跨类并行会产生非确定性干扰；与 RabbitMQ.Tests.Integration / Helpdesk 一致采用程序集串行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]