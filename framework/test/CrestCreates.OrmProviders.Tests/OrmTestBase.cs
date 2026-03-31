using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using SqlSugar;

namespace CrestCreates.OrmProviders.Tests
{
    public abstract class OrmTestBase
    {
        // 测试实体类 - 带审计字段
        public class TestAuditedEntity : AuditedAggregateRoot<long>
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public TestAuditedEntity()
            {
            }

            public TestAuditedEntity(long id, string name, string description)
            {
                Id = id;
                Name = name;
                Description = description;
            }

            public void UpdateName(string name)
            {
                Name = name;
            }
        }

        // 测试实体类 - 带软删除
        public class TestSoftDeleteEntity : FullyAuditedAggregateRoot<long>
        {
            public string Name { get; set; } = string.Empty;

            public TestSoftDeleteEntity()
            {
            }

            public TestSoftDeleteEntity(long id, string name)
            {
                Id = id;
                Name = name;
            }
        }
    }
}