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

            var b1 = new PushButtonData(
                "ErpMappingAll",
                "🔑 Сопоставление кодов 1C\n(проекта)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            var b2 = new PushButtonData(
                "ErpMappingActive",
                "🔑  Сопоставление кодов 1C\n(активной спецификации)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            var b3 = new PushButtonData(
                "ErpExportResourcesActive",
                "📤 Выгрузка спецификации\n(активной спецификации)",
                path,
                "RevitApi_3.ErpExportResourcesActiveCommand");

            panel.AddStackedItems(b1, b2, b3);
            //panel.AddItem(b2);
            //panel.AddItem(b3);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
