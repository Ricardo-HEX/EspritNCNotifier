# ESPRIT NC Notifier

Extensión para ESPRIT EDGE 2026.2. Postprocesa las operaciones seleccionadas (o el programa completo tras confirmación), guarda los NC y una captura isométrica limpia de la pieza en la carpeta `NC` situada junto al documento, y abre un correo preparado en la aplicación predeterminada de Windows.

## Uso

1. Reiniciar ESPRIT EDGE después de instalar la DLL.
2. Seleccionar operaciones y pulsar `Código NC > Código NC y notificación`.
3. Indicar el nombre del NC y el correo destinatario. El último destinatario se recuerda.
4. Tras generar el NC, revisar el mensaje abierto en Outlook, Gmail u otra aplicación configurada. La extensión abre también la carpeta con la captura seleccionada para poder arrastrarla al correo.

El mensaje incluye el documento, las operaciones procesadas, la máquina configurada, las rutas de los NC y la captura. La extensión no solicita ni almacena contraseñas, tokens o credenciales. Utiliza el protocolo `mailto:` de Windows, por lo que no envía automáticamente ni adjunta archivos.
