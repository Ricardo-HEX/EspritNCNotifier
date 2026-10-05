using System.ComponentModel.Composition;
using ESPRIT.NetApi.Extensions;

namespace EspritNcNotifier
{
    [Export(typeof(IExtension))]
    [ExportMetadata("SupportBuild", 20)]
    public sealed class Main : IExtension
    {
        private ExtensionController _controller;
        public string Name => "Postprocesar y notificar";
        public string Description => "Genera ficheros NC junto al documento ESPRIT y notifica por correo.";
        public string Publisher => "Hexagon Custom Solutions";
        public string Url => "";
        public void Connect(object app) => _controller = new ExtensionController((Esprit.Application)app);
        public void Disconnect() { _controller?.Dispose(); _controller = null; }
    }
}
