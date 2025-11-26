using System.Configuration.Assemblies;
using System;
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

            PushButtonData b1 = new PushButtonData(
                "ErpMappingAll",
                "Коды 1C (все)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            PushButtonData b2 = new PushButtonData(
                "ErpMappingActive",
                "Коды 1C (активная)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            addBtnIcon(assemblyPath, "sync_pic.png", b1);
            addBtnIcon(assemblyPath, "download_pic.png", b2);
            panel.AddItem(b1);
            panel.AddItem(b2);

            return Result.Succeeded;
        }
        private bool addBtnIcon(string imgPath, string filename, PushButtonData btn)
        {
            try
            {
                string iconPath = Path.Combine(Path.GetDirectoryName(imgPath), "Resources", filename);
                if (File.Exists(iconPath))
                {
                    var bmp = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                    btn.LargeImage = bmp;
                    return true;
                }
            }
            catch { return false; }
            return false;
        
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
