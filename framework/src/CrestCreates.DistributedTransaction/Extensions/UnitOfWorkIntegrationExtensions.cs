using CrestCreates.Domain.UnitOfWork;
using CrestCreates.DistributedTransaction.Abstractions;

namespace CrestCreates.DistributedTransaction.Extensions
{
    public static class UnitOfWorkIntegrationExtensions
    {
        public static void RegisterWithDistributedTransaction(this IUnitOfWork unitOfWork, IDistributedTransaction transaction)
        {
            // 通过扩展方法为事务添加工作单元参与者
            // 具体实现由各个事务实现提供
            if (transaction is ITransactionParticipantRegistry registry)
            {
                var participant = new UnitOfWorkTransactionParticipant(unitOfWork);
                registry.AddParticipant(participant);
            }
        }
    }

    public interface ITransactionParticipantRegistry
    {
        void AddParticipant(ITransactionParticipant participant);
    }

    internal class UnitOfWorkTransactionParticipant : ITransactionParticipant
    {
        private readonly IUnitOfWork _unitOfWork;

        public UnitOfWorkTransactionParticipant(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> PrepareAsync()
        {
            // 工作单元的准备阶段，这里可以执行一些预提交操作
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task CommitAsync()
        {
            // 工作单元的提交
            await _unitOfWork.CommitTransactionAsync();
        }

        public async Task RollbackAsync()
        {
            // 工作单元的回滚
            await _unitOfWork.RollbackTransactionAsync();
        }

        public async Task CompensateAsync()
        {
            // 工作单元的补偿操作
            await _unitOfWork.RollbackTransactionAsync();
        }
    }
}
