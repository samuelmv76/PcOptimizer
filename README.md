# PC Optimizer

Herramienta de mantenimiento para Windows 10/11. Muestra que esta ocupando
espacio o ralentizando el arranque y deja que **tu** decidas que hacer.

## Que hace

- Limpieza de temporales y cache, con antiguedad minima configurable.
- Gestor de programas al inicio (registro y carpeta Inicio), reversible.
- Analisis de espacio en disco.
- Vista de servicios y procesos.
- Diagnostico de firmware: detecta Secure Boot desactivado, arranque Legacy,
  virtualizacion apagada y RAM funcionando por debajo de su velocidad nominal
  (XMP/EXPO sin activar).

## Que NO hace, a proposito

- **No limpia el registro.** No hay evidencia de que mejore el rendimiento y si
  de que rompe instalaciones.
- **No modifica la BIOS.** Escribir en el firmware requiere un driver en modo
  kernel y puede dejar el equipo inarrancable. El modulo de firmware solo lee
  y te dice que cambiar tu mismo.
- **No promete porcentajes de mejora.** Si una herramienta te dice que tu PC
  esta "un 47% optimizado", te esta mintiendo.

## Principios de diseno

1. **Simular antes de aplicar.** `RemediationOptions.Simulate` es `true` por
   defecto. Hay que pedir explicitamente la ejecucion real.
2. **Punto de restauracion** antes de tocar servicios o arranque.
3. **Registro de auditoria** en `%LOCALAPPDATA%\PcOptimizer\audit.log` con todo
   lo que la aplicacion ha cambiado.
4. **El nucleo no conoce la UI.** `PcOptimizer.Core` no referencia WPF, asi que
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
  Abstractions/          IOptimizerModule, Finding, RemediationOptions
  Cleaning/              limpieza de temporales
  Startup/               programas al inicio
  Safety/                puntos de restauracion
  Auditing/              log de cambios
src/PcOptimizer.App/     WPF
tests/                   xUnit
```

Para anadir un modulo: implementa `IOptimizerModule` en Core y registralo en
`MainViewModel`.

## Aviso

Esta herramienta modifica el sistema. Usala bajo tu responsabilidad y prueba
en una maquina virtual antes de ejecutarla en un equipo que te importe.

## Licencia

GPL-3.0. Ver `LICENSE`.
