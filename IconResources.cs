using System;
using System.Drawing;
using System.Reflection;

namespace EspritNcNotifier
{
    internal static class IconResources
    {
        private const string ResourceName = "EspritNcNotifier.Resources.NcSendIcon.ico";

        public static Icon LoadNcSendIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("No se encontró el icono incrustado: " + ResourceName);

                using (var source = new Icon(stream))
                    return (Icon)source.Clone();
            }
        }
    }
}
