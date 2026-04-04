# 查询扩展方法使用示例

## 基本使用

### Where 方法（等值过滤）

```csharp
using LibraryManagement.Domain.Entities;
using LibraryManagement.QueryExtensions;
using Microsoft.EntityFrameworkCore;

// 等值过滤
var books = await dbContext.Books
    .WhereTitle("C# in Depth")
    .ToListAsync();

// 链式调用（多个条件）
var books = await dbContext.Books
    .WhereAuthor("Jon Skeet")
    .WhereIsAvailable(true)
    .ToListAsync();
```

### 字符串方法（模糊查询）

```csharp
// 包含查询
var books = await dbContext.Books
    .WhereTitleContains("C#")
    .ToListAsync();

// 开头匹配
var books = await dbContext.Books
    .WhereTitleStartsWith("The")
    .ToListAsync();

// 结尾匹配
var books = await dbContext.Books
    .WhereTitleEndsWith("Guide")
    .ToListAsync();
```

### 数值/日期方法（范围查询）

```csharp
// 大于查询
var books = await dbContext.Books
    .WherePriceGreaterThan(50)
    .ToListAsync();

// 小于查询
var books = await dbContext.Books
    .WherePriceLessThan(100)
    .ToListAsync();

// 范围查询
var books = await dbContext.Books
    .WherePriceBetween(30, 80)
    .ToListAsync();

// 日期范围查询
var books = await dbContext.Books
    .WherePublicationDateBetween(new DateTime(2020, 1, 1), new DateTime(2023, 12, 31))
    .ToListAsync();
```

### 排序方法

```csharp
// 升序排序
var books = await dbContext.Books
    .OrderByTitle()
    .ToListAsync();

// 降序排序
var books = await dbContext.Books
    .OrderByPriceDescending()
    .ToListAsync();

// 组合使用
var books = await dbContext.Books
    .WhereIsAvailable(true)
    .OrderByPublicationDateDescending()
    .ThenByTitle()
    .ToListAsync();
```

## 在 Repository 中使用

```csharp
using LibraryManagement.Domain.Entities;
using LibraryManagement.QueryExtensions;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.EntityFrameworkCore.Repositories;

public class BookRepository : BookRepositoryBase, IBookRepository
{
    public BookRepository(LibraryDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<List<Book>> GetBooksByTitleKeywordAsync(string keyword, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .WhereTitleContains(keyword)
            .OrderByTitle()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Book>> GetAvailableBooksByPriceRangeAsync(
        decimal minPrice, 
        decimal maxPrice, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .WhereIsAvailable(true)
            .WherePriceBetween(minPrice, maxPrice)
            .OrderByPrice()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Book>> GetRecentBooksAsync(int count, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .OrderByPublicationDateDescending()
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
```

## 在 Service 中使用

```csharp
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Entities;
using LibraryManagement.QueryExtensions;
using AutoMapper;

namespace LibraryManagement.Application.Services;

public class BookAppService : BookCrudServiceBase, IBookAppService
{
    public BookAppService(
        IBookRepository repository,
        IMapper mapper)
        : base(repository, mapper)
    {
    }

    public async Task<List<BookDto>> SearchBooksAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var query = _repository.GetQueryable();
        
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query
                .WhereTitleContains(keyword);
        }
        
        var books = await query
            .OrderByTitle()
            .ToListAsync(cancellationToken);
            
        return _mapper.Map<List<BookDto>>(books);
    }

    public async Task<PagedResult<BookDto>> GetBooksByFilterAsync(
        string? keyword,
        decimal? minPrice,
        decimal? maxPrice,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _repository.GetQueryable();
        
        // 应用关键字过滤
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.WhereTitleContains(keyword);
        }
        
        // 应用价格范围过滤
        if (minPrice.HasValue && maxPrice.HasValue)
        {
            query = query.WherePriceBetween(minPrice.Value, maxPrice.Value);
        }
        else if (minPrice.HasValue)
        {
            query = query.WherePriceGreaterThan(minPrice.Value);
        }
        else if (maxPrice.HasValue)
        {
            query = query.WherePriceLessThan(maxPrice.Value);
        }
        
        // 获取总数
        var totalCount = await query.CountAsync(cancellationToken);
        
        // 应用分页和排序
        var items = await query
            .OrderByPublicationDateDescending()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
            
        var dtos = _mapper.Map<List<BookDto>>(items);
        return new PagedResult<BookDto>(dtos, totalCount, pageNumber, pageSize);
    }
}
```

## 完整的查询示例

```csharp
using LibraryManagement.Domain.Entities;
using LibraryManagement.QueryExtensions;
using Microsoft.EntityFrameworkCore;

public async Task ComplexQueryExample(LibraryDbContext dbContext)
{
    // 复杂查询示例
    var result = await dbContext.Books
        // 过滤条件
        .WhereIsAvailable(true)
        .WherePriceGreaterThan(20)
        .WherePriceLessThan(100)
        .WhereTitleContains("Programming")
        // 排序
        .OrderByPrice()
        .ThenByPublicationDateDescending()
        // 分页
        .Skip(0)
        .Take(10)
        .ToListAsync();
}
```

## 生成的方法列表

对于每个实体属性，会生成以下方法：

### 所有属性
- `Where{PropertyName}(value)` - 等值过滤

### 字符串属性
- `Where{PropertyName}(value)` - 等值过滤
- `Where{PropertyName}Contains(value)` - 包含查询
- `Where{PropertyName}StartsWith(value)` - 开头匹配
- `Where{PropertyName}EndsWith(value)` - 结尾匹配

### 数值/日期属性
- `Where{PropertyName}(value)` - 等值过滤
- `Where{PropertyName}GreaterThan(value)` - 大于
- `Where{PropertyName}LessThan(value)` - 小于
- `Where{PropertyName}Between(from, to)` - 范围

### 所有属性（排序）
- `OrderBy{PropertyName}()` - 升序
- `OrderBy{PropertyName}Descending()` - 降序
