using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 诊断附件唯一入口：次级清理/终结故障只附加到主异常的可检查通道（<see cref="Exception.Data"/>），
    /// 不改写主异常的类型/栈/消息。
    /// </summary>
    internal static class UnitOfWorkDiagnostics
    {
        internal static void Attach(Exception primary, string key, Exception diagnostic)
        {
            primary.Data[key] = diagnostic;
        }
    }
}
