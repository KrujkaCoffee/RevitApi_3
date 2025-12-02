using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitApi_3
{
    public class RevitItem
    {
        public ElementId ElementId { get; set; }
        public ElementId TypeId { get; set; }

        public string ScheduleName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DisplayName { get; set; }

        public string ErpCode { get; set; }

        // Из спецификации
        public string Unit { get; set; }          // Единица измерения
        public double? MassPerItem { get; set; }  // Масса/шт (из параметров)
    }

    public class ErpItem
    {
        public string Code { get; set; }      // Код 1C-ERP
        public string Name { get; set; }      // Наименование
        public string Extra { get; set; }     // Доп. инфо
        public string Unit { get; set; }      // Ед. изм. из ERP
    }

    // Дерево ERP (классификатор)
    public class ErpTreeNode
    {
        public string RefKey { get; set; }
        public string ParentKey { get; set; }
        public string Description { get; set; }

        public List<ErpTreeNode> Children { get; set; } = new List<ErpTreeNode>();

        public override string ToString() => Description;
    }

    // Агрегированная строка для экспорта
    public class ExportRow
    {
        public string ScheduleName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DisplayName { get; set; }
        public string ErpCode { get; set; }
        public string Unit { get; set; }

        public int Quantity { get; set; }
        public double? MassPerItem { get; set; }
        public double? TotalMass { get; set; }
    }
}
