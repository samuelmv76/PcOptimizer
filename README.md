# PC Optimizer

Herramienta de mantenimiento para Windows 10/11. Muestra que esta ocupando
espacio o ralentizando el arranque y deja que **tu** decidas que hacer.

## Que hace

La aplicacion esta dividida en apartados. Cada uno analiza primero y no toca
nada hasta que tu lo pides.

**Hardware del equipo** (solo lectura). Procesador, grafica con su VRAM real y
la fecha de su driver, modulos de RAM, discos con su tipo (SSD o mecanico) y
version de Windows. Es la base del resto: sin saber que hay dentro no se puede
recomendar nada concreto.

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

**Programas al inicio.** Registro y carpeta Inicio. Desactivar no borra: la
entrada se copia a una clave de respaldo propia.

**Firmware y BIOS** (solo lectura). Modo de arranque UEFI o Legacy, Secure Boot,
virtualizacion, TPM, integridad de memoria y si la RAM esta funcionando por
debajo de su velocidad nominal (XMP/EXPO sin activar). De cada cosa te dice que
cambiar y donde.

## Que NO hace, a proposito

- **No limpia el registro.** No hay evidencia de que mejore el rendimiento y si
  de que rompe instalaciones.
- **No modifica la BIOS.** Escribir en el firmware requiere un driver en modo
  kernel y puede dejar el equipo inarrancable. El modulo de firmware solo lee
  y te dice que cambiar tu mismo.
- **No borra Windows.old ni WinSxS por su cuenta.** Hacerlo a mano falla por
  permisos y deja restos. La aplicacion te ensena cuanto ocupan y cual es la
  forma correcta de quitarlos.
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
  Platform/              WMI, PowerShell, recorrido seguro de carpetas, tamanos
  Hardware/              inventario del equipo
  Cleaning/              limpieza de temporales
  Disk/                  analisis de espacio
  Bloatware/             aplicaciones preinstaladas
  Startup/               programas al inicio
  Firmware/              diagnostico de BIOS
  Safety/                puntos de restauracion
  Auditing/              log de cambios
src/PcOptimizer.App/     WPF
  ViewModels/            estado de la interfaz
  Converters/            severidad a color y etiqueta
tests/                   xUnit
```

Para anadir un modulo: implementa `IOptimizerModule` en Core y registralo en
`MainViewModel`. Si el modulo solo lee, hereda de `DiagnosticModule`: la
interfaz lo detecta por `Kind` y muestra fichas con recomendaciones en vez de
una tabla con casillas, y oculta los botones de aplicar.

## Aviso

Esta herramienta modifica el sistema. Usala bajo tu responsabilidad y prueba
en una maquina virtual antes de ejecutarla en un equipo que te importe.

## Licencia

GPL-3.0. Ver `LICENSE`.
