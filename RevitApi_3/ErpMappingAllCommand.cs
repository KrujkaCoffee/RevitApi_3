using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitApi_3
{
    /// <summary>
    /// Оставлено для совместимости со старыми .addin/ярлыками. Команда больше
    /// не публикуется на ленте, потому что проектное сопоставление неоднозначно.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ErpMappingAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("ERP",
                "Сопоставление по всему проекту отключено. " +
                "Откройте нужную спецификацию и используйте команду сопоставления активной спецификации.");
            return Result.Succeeded;
        }
    }
}
