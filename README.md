# PC Optimizer

Herramienta de mantenimiento para Windows 10/11. Muestra que esta ocupando
espacio o ralentizando el arranque y deja que **tu** decidas que hacer.

## Que hace

La aplicacion esta dividida en apartados. Cada uno analiza primero y no toca
nada hasta que tu lo pides. El analisis de cada apartado se conserva al
cambiar de pagina, con la hora a la que se hizo.

**Resumen.** La pagina de inicio. Al abrir la aplicacion se revisan todos
los apartados a la vez, en paralelo, sin tener que pulsar nada. Las fichas van
apareciendo segun termina cada apartado, y mientras tanto se puede navegar
libremente. Cada ficha dice cuanto hay pendiente y que es lo primero, y lleva a
su apartado con los resultados ya cargados. No cambia nada: es para saber por
donde empezar.

**Hardware del equipo** (solo lectura). Procesador, grafica con su VRAM real y
la fecha de su driver, modulos de RAM, discos con su tipo (SSD o mecanico) y
version de Windows. Es la base del resto: sin saber que hay dentro no se puede
recomendar nada concreto.

**Rendimiento en juegos.** Modo Juego, grabacion en segundo plano, efectos
visuales, transparencia, planificacion acelerada por GPU y plan de energia.
Cada ajuste se recomienda o no **segun el hardware detectado**: bajar los
efectos visuales tiene sentido en un portatil con grafica integrada y no lo
tiene en una torre con una 4070. El apartado tambien dice cuando el cuello de
botella es el hardware y ningun ajuste lo va a arreglar.

**Limpieza de temporales y cache.** Ficheros con mas de 24 horas en un conjunto
cerrado de carpetas del sistema. Nunca entra en Documentos ni en Descargas. Un
fichero de solo lectura, oculto o de sistema se borra igual; uno que otro
programa tiene abierto no se puede borrar en caliente, y con la casilla
"Borrar al reiniciar los ficheros en uso" se programa su borrado para el
proximo arranque, como hacen los instaladores de Windows.

**Espacio en disco.** Que ocupa cada unidad, cache de Windows Update, cache de
Optimizacion de entrega, registros de instalacion y papelera. Lo que esta
aplicacion no puede borrar con garantias (Windows.old, WinSxS, hiberfil.sys)
aparece solo como informacion, con las instrucciones para hacerlo tu.

**Aplicaciones preinstaladas.** Paquetes que vinieron con el equipo o con
Windows. Las piezas del sistema no se listan siquiera, y hay una segunda
comprobacion antes de desinstalar por si la interfaz se equivoca.

**Programas instalados.** Los programas de escritorio clasicos, que es donde
vive el bloatware del fabricante: antivirus de prueba, paneles de HP o Lenovo,
juegos con publicidad. Los drivers y runtimes no se listan siquiera. Cuando el
programa admite desinstalacion silenciosa se usa; cuando no, se avisa de que se
abrira su asistente.

**Programas al inicio.** Registro y carpeta Inicio. Desactivar no borra: la
entrada se copia a una clave de respaldo propia.

**Servicios de Windows.** Servicios que arrancan solos y que la mayoria de la
gente no usa. Lo maximo que hace la aplicacion es pasarlos a inicio **Manual**:
nunca los desactiva ni los para, asi que si algo los necesita Windows puede
levantarlos.

**Firmware y BIOS** (solo lectura). Modo de arranque UEFI o Legacy, Secure Boot,
virtualizacion, TPM, integridad de memoria y si la RAM esta funcionando por
debajo de su velocidad nominal (XMP/EXPO sin activar). De cada cosa te dice que
cambiar y donde.

**Deshacer cambios.** Todo lo que la aplicacion cambia queda anotado con su
valor anterior en `%LOCALAPPDATA%\PcOptimizer\changes.jsonl`. Desde este
apartado se revierte: reactivar un programa de arranque, devolver un servicio a
inicio automatico, restaurar un ajuste de rendimiento o el plan de energia.
Solo se ofrece deshacer lo que la aplicacion sabe deshacer de verdad;
desinstalar un programa no esta en esa lista y la interfaz no finge que si.

## Interfaz

Tema oscuro por defecto, con claro y "como Windows" en **Ajustes** (la
rueda al pie de la barra lateral). El cambio es en vivo: todos los colores
salen de `Themes/Dark.xaml` o `Themes/Light.xaml` y los controles los usan por
`DynamicResource`. La barra de titulo se tine del mismo color que la ventana.

La tipografia es Segoe UI Variable, la del sistema en Windows 11, y los iconos
de la barra lateral salen de Segoe Fluent Icons, que ya trae Windows.

El icono de la aplicacion esta en `src/PcOptimizer.App/Assets/`: el `.svg` es
la fuente, y el `.ico` lleva un dibujo simplificado para 16-20 px (sin las
patillas del chip, que a ese tamano solo serian ruido).

## Que NO hace, a proposito

- **No limpia el registro.** No hay evidencia de que mejore el rendimiento y si
  de que rompe instalaciones.
- **No escribe la configuracion propia de la placa.** XMP, virtualizacion o
  Secure Boot se guardan en un bloque cuyo formato cambia con cada placa y
  version de BIOS; escribirlo a ciegas desde Windows puede dejar el equipo sin
  arrancar. Lo que si hace, en **Arranque y BIOS**, es lo que UEFI preve que
  haga el sistema operativo: abrir la BIOS en el proximo reinicio, arrancar una
  vez desde otro disco (`BootNext`) e integridad de memoria. **Firmware y BIOS**
  dice en que menu de tu placa esta cada ajuste (ASUS, MSI, Gigabyte, ASRock).
- **No borra Windows.old ni WinSxS por su cuenta.** Hacerlo a mano falla por
  permisos y deja restos. La aplicacion te ensena cuanto ocupan y cual es la
  forma correcta de quitarlos.
- **No desactiva servicios.** Como mucho los pasa a Manual. Desactivar el
  servicio equivocado es como se deja un equipo sin sonido o sin red.
- **No incluye ajustes de foro sin evidencia.** El catalogo de rendimiento es
  corto a proposito: cada entrada tiene que hacer algo medible. Si un ajuste
  da resultados dispares, se dice y no viene marcado.
- **No desinstala piezas del sistema.** La Tienda, Defender, los runtimes y los
  servicios que necesitan los juegos estan en una lista de exclusion explicita.
- **No promete porcentajes de mejora.** Si una herramienta te dice que tu PC
  esta "un 47% optimizado", te esta mintiendo.

## Principios de diseno

1. **Simular antes de aplicar.** `RemediationOptions.Simulate` es `true` por
   defecto. Hay que pedir explicitamente la ejecucion real, y Aplicar pide
   confirmacion antes de tocar nada.
2. **Punto de restauracion** antes de tocar registro o programas instalados.
   No se ofrece en los modulos que solo borran ficheros: Restaurar sistema no
   cubre ficheros de usuario ni temporales, asi que ahi solo llenaria el disco
   de puntos inutiles.
3. **Registro de auditoria** en `%LOCALAPPDATA%\PcOptimizer\audit.log` con todo
   lo que la aplicacion ha cambiado y, con la marca `FAILED`, lo que intento
   cambiar y no pudo, con el motivo.
4. **Decir siempre que ha pasado.** Cuando un elemento no se puede aplicar, la
   interfaz dice cual y por que, no solo cuantos fallaron.
5. **El nucleo no conoce la UI.** `PcOptimizer.Core` no referencia WPF, asi que
   se puede testear sin interfaz y anadir una CLI mas adelante.

## Compilar

Requisitos: Windows 10/11, .NET SDK 9.

```powershell
dotnet restore
dotnet build -c Release
dotnet test
```

Ejecutable unico:

```powershell
dotnet publish src/PcOptimizer.App/PcOptimizer.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o publish
```

La aplicacion pide elevacion al arrancar (`app.manifest`). Sin ella, los modulos
que tocan `HKLM` y `C:\Windows\Temp` fallaran con acceso denegado.

## Estructura

```
src/PcOptimizer.Core/    logica, sin UI
  Abstractions/          IOptimizerModule, Finding, ModuleKind, RemediationOptions
  Platform/              WMI, registro, PowerShell, planes de energia, borrado de ficheros
  Hardware/              inventario del equipo y lectura compartida del perfil
  Summary/               pagina de resumen
  Performance/           ajustes de juego segun el hardware
  Cleaning/              limpieza de temporales
  Disk/                  analisis de espacio
  Bloatware/             aplicaciones de la Store
  Programs/              programas de escritorio
  Startup/               programas al inicio
  Services/              servicios de Windows
  Firmware/              diagnostico de BIOS
  Safety/                puntos de restauracion y diario de cambios
  Undo/                  vuelta atras
  Auditing/              log de cambios
  Configuration/         preferencias de la aplicacion
src/PcOptimizer.App/     WPF
  Themes/                paletas oscura y clara, y plantillas de controles
  Theming/               cambio de tema en vivo y barra de titulo
  Assets/                icono
  ViewModels/            estado de la interfaz y ajustes
  Converters/            severidad, iconos de apartado, visibilidad
  Services/              dialogos de confirmacion
tests/                   xUnit
```

Para anadir un modulo: implementa `IOptimizerModule` en Core y registralo en
`MainViewModel`. Si el modulo solo lee, hereda de `DiagnosticModule`: la
interfaz lo detecta por `Kind` y muestra fichas con recomendaciones en vez de
una tabla con casillas, y oculta los botones de aplicar.

Si el modulo cambia algo que se pueda revertir, anota un `ReversibleChange` en
el `IChangeJournal` con el valor anterior, y anade el caso a `UndoModule`. Un
modulo que cambia sin dejar vuelta atras deberia ser la excepcion y decirlo.

## Aviso

Esta herramienta modifica el sistema. Usala bajo tu responsabilidad y prueba
en una maquina virtual antes de ejecutarla en un equipo que te importe.

## Licencia

GPL-3.0. Ver `LICENSE`.
