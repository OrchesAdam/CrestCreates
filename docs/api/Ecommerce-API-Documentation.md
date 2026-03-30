# 电子商务 API 文档

## 概述

本文档描述了电子商务示例项目的API端点，包括产品管理相关的所有接口。

## 基础URL

所有API端点的基础URL为：`/api/Product`

## 认证

目前所有API端点均不需要认证，可直接访问。

## API端点

### 1. 获取产品详情

**端点**: `GET /api/Product/{id}`

**功能**: 根据产品ID获取产品详细信息

**参数**:
- `id` (路径参数): 产品ID，整数

**响应**:
- 成功: `200 OK`，返回ProductDto对象
- 失败: `404 Not Found`，产品不存在

**示例响应**:
```json
{
  "Id": 1,
  "Name": "产品名称",
  "Description": "产品描述",
  "Price": 99.99,
  "Stock": 100,
  "IsActive": true
}
```

### 2. 根据名称获取产品

**端点**: `GET /api/Product/name/{name}`

**功能**: 根据产品名称获取产品详细信息

**参数**:
- `name` (路径参数): 产品名称，字符串

**响应**:
- 成功: `200 OK`，返回ProductDto对象
- 失败: `404 Not Found`，产品不存在

**示例响应**:
```json
{
  "Id": 1,
  "Name": "产品名称",
  "Description": "产品描述",
  "Price": 99.99,
  "Stock": 100,
  "IsActive": true
}
```

### 3. 获取活跃产品列表

**端点**: `GET /api/Product/active`

**功能**: 获取活跃状态的产品列表，支持分页

**参数**:
- `page` (查询参数): 页码，默认值为1
- `pageSize` (查询参数): 每页数量，默认值为10

**响应**:
- 成功: `200 OK`，返回ProductListDto对象

**示例响应**:
```json
{
  "Items": [
    {
      "Id": 1,
      "Name": "产品名称1",
      "Description": "产品描述1",
      "Price": 99.99,
      "Stock": 100,
      "IsActive": true
    },
    {
      "Id": 2,
      "Name": "产品名称2",
      "Description": "产品描述2",
      "Price": 199.99,
      "Stock": 50,
      "IsActive": true
    }
  ],
  "TotalCount": 2,
  "Page": 1,
  "PageSize": 10
}
```

### 4. 获取缺货产品列表

**端点**: `GET /api/Product/out-of-stock`

**功能**: 获取库存为0或负数的产品列表

**响应**:
- 成功: `200 OK`，返回ProductDto对象列表

**示例响应**:
```json
[
  {
    "Id": 3,
    "Name": "缺货产品",
    "Description": "缺货产品描述",
    "Price": 49.99,
    "Stock": 0,
    "IsActive": true
  }
]
```

### 5. 获取平均价格

**端点**: `GET /api/Product/average-price`

**功能**: 获取所有活跃产品的平均价格

**响应**:
- 成功: `200 OK`，返回平均价格（decimal类型）

**示例响应**:
```json
150.50
```

### 6. 创建产品

**端点**: `POST /api/Product`

**功能**: 创建新的产品

**请求体** (CreateProductDto):
```json
{
  "Name": "新产品名称",
  "Description": "新产品描述",
  "Price": 149.99,
  "Stock": 200
}
```

**响应**:
- 成功: `201 Created`，返回创建的ProductDto对象，包含新生成的ID
- 失败: `400 Bad Request`，请求参数错误

**示例响应**:
```json
{
  "Id": 4,
  "Name": "新产品名称",
  "Description": "新产品描述",
  "Price": 149.99,
  "Stock": 200,
  "IsActive": true
}
```

### 7. 更新产品

**端点**: `PUT /api/Product/{id}`

**功能**: 更新指定ID的产品信息

**参数**:
- `id` (路径参数): 产品ID，整数

**请求体** (UpdateProductDto):
```json
{
  "Name": "更新后的产品名称",
  "Description": "更新后的产品描述",
  "Price": 199.99,
  "Stock": 150,
  "IsActive": true
}
```

**响应**:
- 成功: `200 OK`，返回更新后的ProductDto对象
- 失败: `404 Not Found`，产品不存在
- 失败: `400 Bad Request`，请求参数错误

**示例响应**:
```json
{
  "Id": 1,
  "Name": "更新后的产品名称",
  "Description": "更新后的产品描述",
  "Price": 199.99,
  "Stock": 150,
  "IsActive": true
}
```

### 8. 删除产品

**端点**: `DELETE /api/Product/{id}`

**功能**: 删除指定ID的产品

**参数**:
- `id` (路径参数): 产品ID，整数

**响应**:
- 成功: `204 No Content`
- 失败: `404 Not Found`，产品不存在

### 9. 减少库存

**端点**: `POST /api/Product/{id}/reduce-stock`

**功能**: 减少指定产品的库存数量

**参数**:
- `id` (路径参数): 产品ID，整数

**请求体**:
```json
5
```

**响应**:
- 成功: `200 OK`
- 失败: `404 Not Found`，产品不存在
- 失败: `400 Bad Request`，库存不足

### 10. 增加库存

**端点**: `POST /api/Product/{id}/increase-stock`

**功能**: 增加指定产品的库存数量

**参数**:
- `id` (路径参数): 产品ID，整数

**请求体**:
```json
10
```

**响应**:
- 成功: `200 OK`
- 失败: `404 Not Found`，产品不存在

## 数据模型

### ProductDto

```json
{
  "Id": 1,
  "Name": "产品名称",
  "Description": "产品描述",
  "Price": 99.99,
  "Stock": 100,
  "IsActive": true
}
```

### CreateProductDto

```json
{
  "Name": "新产品名称",
  "Description": "新产品描述",
  "Price": 149.99,
  "Stock": 200
}
```

### UpdateProductDto

```json
{
  "Name": "更新后的产品名称",
  "Description": "更新后的产品描述",
  "Price": 199.99,
  "Stock": 150,
  "IsActive": true
}
```

### ProductListDto

```json
{
  "Items": [
    {
      "Id": 1,
      "Name": "产品名称1",
      "Description": "产品描述1",
      "Price": 99.99,
      "Stock": 100,
      "IsActive": true
    }
  ],
  "TotalCount": 1,
  "Page": 1,
  "PageSize": 10
}
```

## 错误处理

| 状态码 | 描述 | 示例响应 |
|--------|------|----------|
| 400 | 请求参数错误 | `{"type": "https://tools.ietf.org/html/rfc7231#section-6.5.1", "title": "Bad Request", "status": 400, "detail": "库存不足"}` |
| 404 | 资源不存在 | `{"type": "https://tools.ietf.org/html/rfc7231#section-6.5.4", "title": "Not Found", "status": 404, "detail": "Product not found"}` |
| 500 | 服务器内部错误 | `{"type": "https://tools.ietf.org/html/rfc7231#section-6.6.1", "title": "Internal Server Error", "status": 500, "detail": "An error occurred while processing your request."}` |

## 示例请求

### 创建产品

```bash
curl -X POST "https://localhost:5001/api/Product" \
  -H "Content-Type: application/json" \
  -d '{
    "Name": "测试产品",
    "Description": "测试产品描述",
    "Price": 99.99,
    "Stock": 100
  }'
```

### 获取产品详情

```bash
curl "https://localhost:5001/api/Product/1"
```

### 更新产品

```bash
curl -X PUT "https://localhost:5001/api/Product/1" \
  -H "Content-Type: application/json" \
  -d '{
    "Name": "更新后的测试产品",
    "Description": "更新后的测试产品描述",
    "Price": 149.99,
    "Stock": 150,
    "IsActive": true
  }'
```

### 删除产品

```bash
curl -X DELETE "https://localhost:5001/api/Product/1"
```

### 减少库存

```bash
curl -X POST "https://localhost:5001/api/Product/1/reduce-stock" \
  -H "Content-Type: application/json" \
  -d '5'
```

### 增加库存

```bash
curl -X POST "https://localhost:5001/api/Product/1/increase-stock" \
  -H "Content-Type: application/json" \
  -d '10'
```