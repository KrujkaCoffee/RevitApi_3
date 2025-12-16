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

        public string Unit { get; set; }
        public double? MassPerItem { get; set; }
    }

    public class ErpItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Extra { get; set; }
        public string Unit { get; set; }
    }

    // Узел дерева классификатора
    public class ErpTreeNode
    {
        public string RefKey { get; set; }
        public string ParentKey { get; set; }
        public string Description { get; set; }

        public List<ErpTreeNode> Children { get; set; } = new List<ErpTreeNode>();

        public override string ToString() => Description;
    }

    // Элемент справочника (тип номенклатуры, единица и т.п.)
    public class RefNamedItem
    {
        public string RefKey { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }

    // Агрегированная строка для экспортируемой ресурсной
    public class ExportRow
    {
        public string Stage { get; set; } // Этап
        public string ScheduleName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DisplayName { get; set; }
        public string ErpCode { get; set; }
        public string Unit { get; set; }



        public string QuantityText { get; set; }
        public string MassPerItemText { get; set; }
        public string TotalMassText { get; set; }
    }


    public class ScheduleExportRow
    {
        public RefNamedItem StageItem { get; set; }   // выбирается пользователем

        public List<string> Values { get; set; } = new List<string>(); // по индексам колонок
        public string ErpCode { get; set; }          // для валидации/подсветки
        public string Unit { get; set; }             // полезно для последующих операций
    }

    public class ScheduleExportTable
    {
        public List<string> Headers { get; set; } = new List<string>();
        public List<ScheduleExportRow> Rows { get; set; } = new List<ScheduleExportRow>();

        public int ErpCodeCol { get; set; } = -1;
        public int UnitCol { get; set; } = -1;
    }


    public class ValidateResouceFieldsMessages
    {
        public string creator { get; set; } = "";
        public string title { get; set; } = "";
        public string output_dse { get; set; } = "";
        public string date_start { get; set; } = "";
    }

    public class ValidateResourceResponse
    { 
        public ValidateResouceFieldsMessages fields { get; set; }
        public List<string> global { get; set; }
    
    }
}
