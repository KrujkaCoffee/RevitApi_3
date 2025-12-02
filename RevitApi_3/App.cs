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
                "🔑 Коды\n(все)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            var b2 = new PushButtonData(
                "ErpMappingActive",
                "🔑 Коды\n(активная)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            var b3 = new PushButtonData(
                "ErpExportResourcesAll",
                "📤 Выгрузка\n(все)",
                path,
                "RevitApi_3.ErpExportResourcesCommand");

            var b4 = new PushButtonData(
                "ErpExportResourcesActive",
                "📤 Выгрузка\n(активная)",
                path,
                "RevitApi_3.ErpExportResourcesActiveCommand");

            panel.AddItem(b1);
            panel.AddItem(b2);
            panel.AddItem(b3);
            panel.AddItem(b4);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
