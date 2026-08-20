-- Crea un login de SQL Server dedicado a este servicio, con permiso de
-- SOLO LECTURA (db_datareader) sobre db_econo. Nunca uses el usuario admin
-- de la base de datos en el servicio HTTP expuesto a internet: si el
-- servicio o su API key se ven comprometidos, este login limita el daño a
-- "puede leer", nunca a "puede escribir o borrar".
--
-- Ejecutar como administrador de SQL Server, reemplazando la contraseña.

USE master;
GO

CREATE LOGIN econodat_mcp_readonly
    WITH PASSWORD = 'REEMPLAZAR_POR_UNA_CLAVE_FUERTE_UNICA';
GO

USE db_econo;
GO

CREATE USER econodat_mcp_readonly FOR LOGIN econodat_mcp_readonly;
GO

-- db_datareader alcanza para las tools ListarTablas / DescribirTabla /
-- EjecutarConsultaSelect. Si más adelante se restringe a tablas puntuales
-- en vez de toda la base, reemplazar esto por GRANT SELECT tabla por tabla
-- y quitar db_datareader.
ALTER ROLE db_datareader ADD MEMBER econodat_mcp_readonly;
GO

-- Verificación: este login NO debe poder escribir. Confirmar que lo
-- siguiente falla con "permiso denegado" antes de dar por cerrado el setup.
-- (ejecutar conectado como econodat_mcp_readonly)
-- INSERT INTO OrdenServicio (...) VALUES (...);
