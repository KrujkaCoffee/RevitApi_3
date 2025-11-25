using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitErpIntegration
{
    public class RevitItem
    {
        public ElementId ElementId { get; set; }
        public ElementId TypeId { get; set; }
        public string ScheduleName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DisplayName { get; set; } // ADSK_Наименование и т.п.
        public string ErpCode { get; set; }     // Код 1C-ERP
    }

    public class ErpItem
    {
        public string Code { get; set; } // Код 1C-ERP
        public string Name { get; set; } // Наименование из 1С
        public string Extra { get; set; } // Доп. инфа (по желанию)
    }

    public class ScheduleRow
    {
        public string ScheduleName { get; set; }
        public Dictionary<string, string> Columns { get; set; }

        public ScheduleRow()
        {
            Columns = new Dictionary<string, string>();
        }
    }
}
