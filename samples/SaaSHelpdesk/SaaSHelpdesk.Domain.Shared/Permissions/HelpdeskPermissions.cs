using CrestCreates.Domain.Shared.Permissions;

namespace SaaSHelpdesk.Domain.Shared.Permissions;

public static class HelpdeskPermissions
{
    public const string GroupName = "Helpdesk";

    // 工单管理
    public static class Tickets
    {
        public const string Default = GroupName + ".Tickets";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Assign = Default + ".Assign";
        public const string ChangeStatus = Default + ".ChangeStatus";
        public const string ViewAll = Default + ".ViewAll";
    }

    // 客户管理
    public static class Customers
    {
        public const string Default = GroupName + ".Customers";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ViewAll = Default + ".ViewAll";
    }

    // 分类管理
    public static class Categories
    {
        public const string Default = GroupName + ".Categories";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    // 知识库
    public static class KnowledgeBase
    {
        public const string Default = GroupName + ".KnowledgeBase";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Publish = Default + ".Publish";
    }

    // SLA 策略
    public static class SLAPolicies
    {
        public const string Default = GroupName + ".SLAPolicies";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    // 报表
    public static class Reports
    {
        public const string Default = GroupName + ".Reports";
        public const string View = Default + ".View";
        public const string Export = Default + ".Export";
    }
}
