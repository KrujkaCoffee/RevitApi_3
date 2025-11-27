using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
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
                "Коды 1C\n(все)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            var b2 = new PushButtonData(
                "ErpMappingActive",
                "Коды 1C\n(активная)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            var b3 = new PushButtonData(
                "ErpExportResources",
                "Выгрузить\nв ERP",
                path,
                "RevitApi_3.ErpExportResourcesCommand");

            b1.LargeImage = LoadPng("RevitApi_3.Resources.download_pic.png");
            b1.Image = LoadPng("RevitApi_3.Resources.sync_pic.png");

            panel.AddItem(b1);
            panel.AddItem(b2);
            panel.AddItem(b3);


            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        private static BitmapImage LoadPng(string resourcePath)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resourcePath))
            {
                if (s == null) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = s;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
        }
    }

}
