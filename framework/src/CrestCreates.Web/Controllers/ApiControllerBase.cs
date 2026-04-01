using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Net;

namespace CrestCreates.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// 返回成功响应
        /// </summary>
        /// <typeparam name="T">响应数据类型</typeparam>
        /// <param name="data">响应数据</param>
        /// <param name="message">响应消息</param>
        /// <returns>成功响应</returns>
        protected ActionResult<ApiResponse<T>> Success<T>(T data, string message = "操作成功")
        {
            return Ok(new ApiResponse<T>
            {
                Code = (int)HttpStatusCode.OK,
                Message = message,
                Data = data
            });
        }

        /// <summary>
        /// 返回成功响应（无数据）
        /// </summary>
        /// <param name="message">响应消息</param>
        /// <returns>成功响应</returns>
        protected ActionResult<ApiResponse> Success(string message = "操作成功")
        {
            return Ok(new ApiResponse
            {
                Code = (int)HttpStatusCode.OK,
                Message = message
            });
        }

        /// <summary>
        /// 返回错误响应
        /// </summary>
        /// <param name="code">错误代码</param>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> Error(int code, string message)
        {
            return StatusCode(code, new ApiResponse
            {
                Code = code,
                Message = message
            });
        }

        /// <summary>
        /// 返回错误响应（400）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> BadRequest(string message = "请求参数错误")
        {
            return Error((int)HttpStatusCode.BadRequest, message);
        }

        /// <summary>
        /// 返回错误响应（401）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> Unauthorized(string message = "未授权")
        {
            return Error((int)HttpStatusCode.Unauthorized, message);
        }

        /// <summary>
        /// 返回错误响应（403）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> Forbidden(string message = "禁止访问")
        {
            return Error((int)HttpStatusCode.Forbidden, message);
        }

        /// <summary>
        /// 返回错误响应（404）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> NotFound(string message = "资源不存在")
        {
            return Error((int)HttpStatusCode.NotFound, message);
        }

        /// <summary>
        /// 返回错误响应（500）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>错误响应</returns>
        protected ActionResult<ApiResponse> InternalServerError(string message = "服务器内部错误")
        {
            return Error((int)HttpStatusCode.InternalServerError, message);
        }
    }

    /// <summary>
    /// API响应基类
    /// </summary>
    public class ApiResponse
    {
        /// <summary>
        /// 响应代码
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// API响应泛型类
    /// </summary>
    /// <typeparam name="T">响应数据类型</typeparam>
    public class ApiResponse<T> : ApiResponse
    {
        /// <summary>
        /// 响应数据
        /// </summary>
        public T Data { get; set; }
    }

    /// <summary>
    /// 分页响应类
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResponse<T> : ApiResponse
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<T> Items { get; set; }

        /// <summary>
        /// 总记录数
        /// </summary>
        public long Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
    }

    /// <summary>
    /// 分页请求参数
    /// </summary>
    public class PagedRequest
    {
        /// <summary>
        /// 页码
        /// </summary>
        [FromQuery(Name = "page")]
        public int Page { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// </summary>
        [FromQuery(Name = "pageSize")]
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// 排序字段
        /// </summary>
        [FromQuery(Name = "sortBy")]
        public string SortBy { get; set; }

        /// <summary>
        /// 排序方向
        /// </summary>
        [FromQuery(Name = "sortDirection")]
        public string SortDirection { get; set; } = "asc";
    }
}