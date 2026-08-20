# MCP server para db_econo (Econocable)

Servicio HTTPS de **solo lectura** que expone la base de datos SQL Server
(`db_econo`) como un MCP server, para que Claude (u otro cliente MCP) pueda
consultarla sin que el puerto 1433 quede expuesto a internet.

```
Internet (HTTPS/443) → IIS (binding TLS) → este servicio (ASP.NET Core, in-process)
                                                    ↓ SQL (red interna)
                                          SQL Server 192.168.0.82:1433 (db_econo)
```

## Por qué existe

Claude Code, corriendo en su entorno remoto, no puede abrir conexiones TCP
crudas a bases de datos (política de red del entorno) ni alcanzar IPs
privadas como `192.168.0.82`. Este servicio resuelve ambos problemas: habla
HTTPS hacia afuera (sí permitido) y SQL hacia adentro, en tu propia red.

## Qué expone

Tres tools, todas de solo lectura:

- `ListarTablas` — lista tablas de `db_econo`.
- `DescribirTabla(tabla, esquema)` — columnas de una tabla puntual.
- `EjecutarConsultaSelect(sql)` — ejecuta un único `SELECT` (rechaza
  `INSERT`/`UPDATE`/`DELETE`/`DROP`/`ALTER`/`EXEC`/multi-sentencia), tope de
  200 filas por llamada.

La defensa real de "solo lectura" no es el código — es el login de SQL
Server que crea `scripts/create_readonly_login.sql`, que solo tiene permiso
`db_datareader`. Aunque hubiera un bug en la validación del código, ese login
no puede escribir nada.

## Requisitos previos en el Windows Server

- **.NET 8 Hosting Bundle** instalado (incluye el módulo ASP.NET Core para
  IIS). Descargar desde el sitio oficial de .NET e instalar; reiniciar IIS
  después (`iisreset`).
- Certificado TLS válido para el dominio/subdominio que vas a usar (no
  autofirmado, para que Claude/clientes externos confíen en él).
- Un login de SQL Server de solo lectura (siguiente paso).

## 1. Crear el login de solo lectura en SQL Server

Ejecuta `scripts/create_readonly_login.sql` en SQL Server Management Studio
(o `sqlcmd`), reemplazando la contraseña de ejemplo por una fuerte y única
para este servicio.

## 2. Primer build

Este repo no tiene acceso a NuGet.org, así que `EconodatMcpServer.csproj`
trae versiones de paquete escritas a mano y **pueden estar desactualizadas**.
En el servidor (o tu máquina de desarrollo), dentro de esta carpeta:

```powershell
dotnet restore
# si falla por versión de paquete, corre:
dotnet add package ModelContextProtocol.AspNetCore --prerelease
dotnet add package Microsoft.Data.SqlClient
dotnet build
```

## 3. Publicar

```powershell
dotnet publish -c Release -o C:\inetpub\wwwroot\econodat-mcp
```

Esto copia el build (incluyendo `web.config`) a la carpeta que usará el sitio
de IIS.

## 4. Configurar el sitio en IIS

1. Crea un **Application Pool** nuevo (ej. `econodat-mcp-pool`), .NET CLR
   version "No Managed Code" (lo maneja el módulo ASP.NET Core, no el CLR de
   IIS clásico).
2. En ese Application Pool, agrega las variables de entorno (pestaña
   "Environment Variables" del pool, o vía `appcmd`/PowerShell
   `Set-ItemProperty` sobre `IIS:\AppPools\...`):
   - `DB_HOST` = `192.168.0.82`
   - `DB_PORT` = `1433`
   - `DB_NAME` = `db_econo`
   - `DB_USER` = `econodat_mcp_readonly`
   - `DB_PASSWORD` = la contraseña que puso en el paso 1
   - `ECONODAT_API_KEY` = un secreto largo generado aleatoriamente (ej.
     `openssl rand -hex 32` o `[System.Guid]::NewGuid().ToString() + [System.Guid]::NewGuid().ToString()`
     en PowerShell) — esta es la clave que Claude usará para autenticarse.
3. Crea un **sitio** (o aplicación bajo un sitio existente) apuntando a
   `C:\inetpub\wwwroot\econodat-mcp`, usando ese Application Pool.
4. Agrega el **binding HTTPS** con tu certificado, en el puerto 443, con el
   hostname que vas a usar (ej. `mcp-econodat.econocable.pe`).
5. Asegúrate que el firewall de Windows y el de red permitan entrada en el
   443 hacia este sitio, y que el DNS de ese hostname apunte a la IP pública
   correspondiente.

## 5. Probar

```powershell
curl https://mcp-econodat.econocable.pe/health
# esperado: healthy

curl -H "X-Api-Key: TU_API_KEY" https://mcp-econodat.econocable.pe/mcp
```

Para una prueba más completa del protocolo MCP, usa el
[MCP Inspector](https://modelcontextprotocol.io) desde cualquier máquina con
Node: `npx @modelcontextprotocol/inspector`, apuntando a
`https://mcp-econodat.econocable.pe/mcp` con el header `X-Api-Key`.

## 6. Conectarlo a Claude

- **Como conector de organización**: en `claude.ai/settings` → Connectors →
  "Add custom connector", pega la URL `https://mcp-econodat.econocable.pe/mcp`
  y el header de autenticación. Un admin de la organización debe hacerlo.
- **En este entorno de Claude Code**: agrega la URL y la API key en la
  configuración de conectores/MCP de este entorno (no en el chat), para que
  quede disponible sin volver a pegar el secreto aquí.

## Seguridad — antes de dar esto por "listo para producción"

- [ ] El login de SQL Server (`econodat_mcp_readonly`) confirmado sin
      permisos de escritura (ver verificación al final del script SQL).
- [ ] `ECONODAT_API_KEY` es un valor aleatorio largo, no una palabra
      memorable, y no está en ningún commit de git.
- [ ] El certificado TLS del sitio es válido (no autofirmado) si vas a
      registrarlo como conector de organización.
- [ ] Considera además restringir por IP en el firewall (allowlist), como
      capa adicional además de la API key.
- [ ] Revisa periódicamente `logs\stdout` (configurado en `web.config`) para
      detectar intentos de acceso con API key inválida.
- [ ] Rota `ECONODAT_API_KEY` si alguna vez se pegó en un chat, ticket o log
      no cifrado.
