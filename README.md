# SubastaYa

Trabajo práctico de una plataforma web de subastas, con catálogo, pujas,
billetera virtual y actualizaciones en tiempo real.

## Tecnologías

- Backend: ASP.NET Core Web API, .NET 8 y Entity Framework Core.
- Base de datos: SQL Server; el procedimiento local documentado utiliza LocalDB.
- Tiempo real: SignalR.
- Documentación REST: Swagger / OpenAPI.
- Frontend: HTML, CSS, JavaScript vanilla, Bootstrap y SignalR Client.
  No requiere npm ni compilación; Bootstrap y SignalR se cargan desde CDN.

## Estructura

```text
SubastaYa/
├── SubastaYa.API/
├── SubastaYa.Web/
├── tests/
│   └── stress-concurrency.ps1
├── SubastaYa.sln
└── README.md
```

## Requisitos para el procedimiento local

- Windows, .NET 8 SDK y SQL Server LocalDB.
- PowerShell.
- Entity Framework Core CLI (`dotnet-ef`, versión 8).
- Navegador y servidor HTTP local, por ejemplo Live Server de VS Code.
- Acceso a los CDN utilizados por el frontend.
- Para el stress test: `sqlcmd` y cURL (`curl.exe`) disponibles en PATH.

Comprobaciones:

```powershell
dotnet --version
dotnet ef --version
Get-Command sqlcmd
Get-Command curl.exe
```

Si falta la herramienta de EF Core:

```powershell
dotnet tool install --global dotnet-ef --version "8.*"
```

## Preparación y ejecución

### 1. Clonar y restaurar

```powershell
git clone https://github.com/candela1805/SubastaYa.git
cd SubastaYa
dotnet restore SubastaYa.sln
```

Los siguientes comandos se ejecutan desde la raíz del repositorio.

### 2. Compilar

```powershell
dotnet build SubastaYa.sln
```

Detener previamente la API si Windows bloquea los archivos de salida de Debug.
También puede comprobarse la configuración Release:

```powershell
dotnet build SubastaYa.sln --configuration Release
```

### 3. Aplicar migraciones

La conexión configurada en `SubastaYa.API/appsettings.json` utiliza
`(localdb)\MSSQLLocalDB`, base `SubastaYaDb`, y autenticación integrada de Windows.

```powershell
dotnet ef database update --project .\SubastaYa.API
```

Este comando modifica la base de datos. Debe ejecutarse antes de iniciar la API
desde un clon limpio. El arranque de Development prepara el usuario demo y su
billetera cuando la autenticación de desarrollo está habilitada; no aplica las
migraciones automáticamente.

### 4. Iniciar el backend

```powershell
dotnet run --project .\SubastaYa.API
```

El perfil `http` de `launchSettings.json` define:

- API: `http://localhost:5000`.
- Entorno: `Development`.

Mantener esa terminal abierta. Estos valores corresponden al perfil local,
no a una configuración de despliegue productivo.

### 5. Iniciar el frontend

Abrir **SubastaYa.Web como carpeta raíz** en VS Code y ejecutar Live Server sobre
`index.html`, usando el puerto 5500. Así el sitio se sirve directamente en:

- `http://localhost:5500/`
- `http://127.0.0.1:5500/`

Si el servidor usa la raíz del repositorio, la URL puede ser
`http://localhost:5500/SubastaYa.Web/index.html`, en lugar de `/`.

En Development, CORS permite los dos orígenes anteriores, cualquier header y
método, y credenciales. La política se aplica solo en Development.
El frontend apunta a la API en `http://localhost:5000`.

## Swagger / OpenAPI

Habilitado únicamente en Development:

- Interfaz: `http://localhost:5000/swagger`.
- Documento: `http://localhost:5000/swagger/v1/swagger.json`.

Endpoints REST:

```text
GET  /api/auctions
POST /api/auctions
GET  /api/auctions/{subastaId}/bids
POST /api/auctions/{subastaId}/bids
GET  /api/auctions/{subastaId}/bids/state
GET  /api/wallet/balance
POST /api/wallet/deposit
```

## SignalR y reglas de pujas

El Hub está en `/hubs/auctions`; los clientes entran y salen de grupos por subasta.
El backend publica `BidPlaced` y `AuctionStateChanged`.

Las pujas actualizan precio, retenciones, ledger y auditoría dentro de una
transacción. Subastas y billeteras usan rowversion de SQL Server para concurrencia
optimista. Una excepción de concurrencia se transforma en HTTP 409 con
`code = BID_CONFLICT`.

Una puja normal conserva `FechaFinUtc`. Una puja válida en los últimos 60 segundos
extiende esa fecha dos minutos. La respuesta y SignalR incluyen la fecha real.
Un Worker procesa los cambios de estado de las subastas.

## Autenticación de Development

Configuración: `SubastaYa.API/appsettings.Development.json`.
Requiere entorno Development y `DevelopmentAuthentication:Enabled = true`.

Sin header se utiliza el usuario demo configurado. Para simular otro usuario:

```text
X-User-Id: <GUID-del-usuario>
```

Un GUID inválido o vacío se rechaza. El usuario seleccionado debe existir y tener
los datos necesarios para la operación; el handler no crea usuarios ni consulta
su existencia.

`NameIdentifier` contiene el ID seleccionado. Para un usuario distinto del demo,
`Name` es ficticio (`Usuario de prueba <GUID>`) y no se emite el email del demo.
Para el demo se mantienen nombre y email configurados.

Este mecanismo permite suplantar identidades deliberadamente para pruebas:
no exponer una API en Development a redes no confiables. En Production no
autentica, aunque se envíe el header; no sustituye un sistema de autenticación
productivo.

## Prueba de concurrencia

Script: `tests/stress-concurrency.ps1`.

### Requisitos específicos

- Migraciones aplicadas.
- `sqlcmd` y `curl.exe` disponibles en PATH.
- LocalDB accesible en `(localdb)\MSSQLLocalDB`, base `SubastaYaDb`.
- API ejecutándose en `http://localhost:5000`, conectada a esa misma base.
- Development y `DevelopmentAuthentication:Enabled = true`.
- PowerShell con permiso para ejecutar scripts locales según la política del equipo.

Desde la raíz:

```powershell
powershell.exe -NoProfile -File .\tests\stress-concurrency.ps1
$LASTEXITCODE
```

### Funcionamiento y criterio de éxito

1. Comprueba las herramientas.
2. Inserta usuarios y billeteras ausentes, serializando la preparación entre
   ejecuciones del script. No modifica saldos de billeteras existentes.
3. Comprueba que ambos postores tengan al menos 12.000 disponibles.
4. Crea automáticamente una subasta nueva: precio inicial 10.000, incremento
   mínimo 1.000, inicio pasado y cierre diez minutos después.
5. Prepara dos jobs con postores diferentes y requests de 12.000.
6. Espera a que ambos estén listos y los libera mediante una barrera común.
7. Envía las pujas mediante cURL y valida los resultados.

El exit code es **0 exclusivamente** cuando hay exactamente dos resultados:

- Un HTTP 200 con JSON válido, `subastaId` igual al ID creado por el script y
  `monto = 12000`.
- Un HTTP 409 con JSON válido y `code = BID_CONFLICT`.

Un 409 con `BID_STATE_INCONSISTENT` u otro código **no es éxito**.
Los demás errores o resultados no concluyentes devuelven exit code 1.
Los postores pueden intercambiar los resultados.

Los temporales están en un directorio único por ejecución. Los jobs y temporales
se limpian en `finally` durante la finalización normal o por error del script;
una terminación forzada del proceso puede impedir esa limpieza.

Timeouts: SQL login 10 s y consulta 20 s; creación HTTP 30 s; preparación de jobs
20 s; espera del worker por la barrera 30 s; cURL conexión 10 s y operación 30 s;
espera conjunta de jobs 45 s.

### Limitaciones

La barrera sincroniza el despacho, no las lecturas de EF Core. El sistema operativo
y la red no garantizan llegada en el mismo milisegundo ni solapamiento dentro del
servidor. Si la segunda puja lee el precio ya actualizado, puede devolver HTTP 400
por mínimo insuficiente: esa ejecución es no concluyente, no un fallo de rowversion.
No hay reintentos automáticos; una nueva ejecución crea otra subasta.

La prueba deja usuarios, billeteras, subastas y las pujas/retenciones confirmadas
en la base. No elimina datos ni repone saldo. Ejecuciones repetidas pueden agotar
el disponible. Debe utilizarse una base local de pruebas, no datos productivos.

Dos ejecuciones no comparten temporales ni AuctionId, pero sí usuarios de prueba
y billeteras. Pueden competir por su rowversion o sus fondos. Para una medición
aislada de la subasta, ejecutar una sola instancia del test a la vez.

El script valida respuestas HTTP/JSON; no realiza una comprobación posterior del
ledger ni del número de filas confirmadas.

## Flujo rápido

Desde la raíz, en una terminal:

```powershell
dotnet restore SubastaYa.sln
dotnet build SubastaYa.sln
dotnet ef database update --project .\SubastaYa.API
dotnet run --project .\SubastaYa.API
```

Luego servir `SubastaYa.Web` en el puerto 5500 y abrir el frontend o Swagger.
Con la API activa, ejecutar el stress test desde otra terminal si se desea
preparar y modificar únicamente datos locales de prueba.
