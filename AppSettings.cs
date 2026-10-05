using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EspritNcNotifier
{
    internal sealed class AppSettings
    {
        public string LastRecipient { get; set; } = "";
        private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EspritNcNotifier");
        private static string FileName => Path.Combine(Folder, "settings.json");
        public static AppSettings Load()
        {
            try { return File.Exists(FileName) ? new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(FileName, Encoding.UTF8)) ?? new AppSettings() : new AppSettings(); }
            catch { return new AppSettings(); }
        }
        public void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(FileName, new JavaScriptSerializer().Serialize(this), Encoding.UTF8); }
    }
}
