# CrestCreates Project

CrestCreates is a modular framework based on ASP.NET Core and Source Generators, providing capabilities for Domain-Driven Design and Infrastructure as Code.

## Project Structure

The project is organized into several modules, each with its own domain, application, and infrastructure layers. Below is a brief overview of the main components:

- **Domain Layer**: Contains the core business logic, including entities, value objects, repositories, and domain events.
- **Application Layer**: Provides application services, data transfer objects (DTOs), and AutoMapper profiles for mapping between entities and DTOs.
- **Infrastructure Layer**: Implements data access using various ORM frameworks (Entity Framework Core, SqlSugar, FreeSql) and includes caching and event bus functionalities.
- **Web Layer**: Contains the web application components, including controllers and middleware.
- **Modules**: Supports modular architecture, allowing for the addition of new features and functionalities through separate modules.

## Features

- Domain-Driven Design (DDD) support with entities, value objects, and domain events.
- Full Unit of Work (UoW) implementation for transaction management and change tracking.
- Multi-ORM support, including Entity Framework Core, SqlSugar, and FreeSql.
- Built-in caching and event bus mechanisms.
- Modular architecture with support for dependency resolution and lifecycle management.

## Getting Started

To get started with the CrestCreates framework, clone the repository and restore the dependencies:

```bash
git clone <repository-url>
cd CrestCreates
dotnet restore
```

You can then build the solution and run the application:

```bash
dotnet build
dotnet run --project src/CrestCreates.Web/CrestCreates.Web.csproj
```

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any suggestions or improvements.

## License

This project is licensed under the MIT License. See the LICENSE file for more details.