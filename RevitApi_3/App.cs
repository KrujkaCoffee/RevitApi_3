using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitApi_3
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "ERP";
            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "1C Integration");

            string path = Assembly.GetExecutingAssembly().Location;

            var b2 = new PushButtonData(
                "ErpMappingActive",
                "🔑 Таблица сопоставления кодов 1C\n(по выбранной спецификации)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            var b3 = new PushButtonData(
                "ErpExportResourcesActive",
                "📤 Выгрузка спецификации\n(выбранной спецификации)",
                path,
                "RevitApi_3.ErpExportResourcesActiveCommand");

            // Проектное сопоставление убрано из интерфейса: работа всегда
            // выполняется в контексте явно открытой спецификации.
            panel.AddStackedItems(b2, b3);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
