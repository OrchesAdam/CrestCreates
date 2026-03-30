using Microsoft.AspNetCore.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 添加SQL Server数据库
builder.AddSqlServer("sqlserver")
    .WithVolumeMount("sql-data", "/var/opt/mssql")
    .WithPassword("your-strong-password-here");

// 添加Redis缓存
builder.AddRedis("redis");

// 添加RabbitMQ消息队列
builder.AddRabbitMQ("rabbitmq");

// 添加电子商务Web应用
var ecommerce = builder.AddProject<Projects.Ecommerce_Web>("ecommerce")
    .WithReference(builder.GetConnectionString("sqlserver"))
    .WithReference(builder.GetConnectionString("redis"))
    .WithReference(builder.GetConnectionString("rabbitmq"));

// 添加健康检查
ecommerce.WithHealthCheck();

var app = builder.Build();
app.Run();