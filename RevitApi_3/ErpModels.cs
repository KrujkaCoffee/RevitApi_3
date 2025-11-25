using Autodesk.Revit.DB;

namespace RevitApi_3
{
    public class RevitItem
    {
        public ElementId ElementId { get; set; }
        public ElementId TypeId { get; set; }
        public string ScheduleName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DisplayName { get; set; } // ADSK_Наименование / тип
        public string ErpCode { get; set; }     // Код 1C-ERP
    }

    public class ErpItem
    {
        public string Code { get; set; } // Код 1C-ERP
        public string Name { get; set; } // Наименование из 1С
        public string Extra { get; set; } // Доп. описание / единицы и т.п.
    }
}
