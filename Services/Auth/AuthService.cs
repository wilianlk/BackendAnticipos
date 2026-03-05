using IBM.Data.Db2;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackendAnticipos.Services.Auth
{
    public class AuthService
    {
        private readonly string _connectionString;

        public AuthService(IConfiguration configuration, IWebHostEnvironment env)
        {
            _connectionString = env.IsDevelopment()
                ? configuration.GetConnectionString("InformixConnection")
                : configuration.GetConnectionString("InformixConnectionProduction");
        }

        public async Task<(bool IsValid, List<string> Roles, int? IdUsuario, string Usuario, string Correo)>
            ValidarUsuarioAsync(string correo, string password)
        {
            correo = correo?.Trim();
            password = password?.Trim();

            if (string.IsNullOrWhiteSpace(correo) || !correo.ToLowerInvariant().EndsWith("@recamier.com"))
                return (false, new List<string>(), null, null, null);

            if (string.IsNullOrWhiteSpace(password))
                return (false, new List<string>(), null, null, null);

            const string sql = @"
                SELECT u.id_usuario, u.usuario, u.correo, r.nombre_rol
                FROM usuarios_anticipo u
                JOIN usuario_rol_anticipo ur ON u.id_usuario = ur.id_usuario
                JOIN roles_anticipos r ON ur.id_rol = r.id_rol
                WHERE TRIM(u.correo) = ? AND TRIM(u.clave) = ?";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.Add(new DB2Parameter { Value = correo });
                cmd.Parameters.Add(new DB2Parameter { Value = password });

                using var reader = await cmd.ExecuteReaderAsync();

                var roles = new List<string>();
                int? idUsuario = null;
                string usuario = null;
                string correoDb = null;

                while (await reader.ReadAsync())
                {
                    idUsuario = Convert.ToInt32(reader["id_usuario"]);
                    usuario = reader["usuario"]?.ToString()?.Trim();
                    correoDb = reader["correo"]?.ToString()?.Trim();
                    var rol = reader["nombre_rol"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(rol))
                        roles.Add(char.ToUpperInvariant(rol[0]) + rol.Substring(1).ToLowerInvariant());
                }

                if (idUsuario.HasValue && roles.Count > 0)
                    return (true, roles, idUsuario, usuario, correoDb);

                return (false, new List<string>(), null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al validar usuario con correo: {Correo}", correo);
                return (false, new List<string>(), null, null, null);
            }
        }

        public async Task<(bool IsValid, List<string> Roles, int? IdUsuario, string Usuario, string Correo)>
            ValidarUsuarioPorSsoCodeAsync(string code)
        {
            code = code?.Trim();

            Log.Information("[SSO] Iniciando validación con code: {Code}", code?[..Math.Min(10, code?.Length ?? 0)] + "...");

            if (string.IsNullOrWhiteSpace(code))
            {
                Log.Warning("[SSO] Code vacío o nulo");
                return (false, new List<string>(), null, null, null);
            }

            const string sqlCode = @"
                SELECT TRIM(usuario_cedula)
                FROM sirii_sso_codes
                WHERE code = ?
                  AND consumed_at_utc IS NULL
                  AND expires_at_utc > CURRENT
            ";

            const string sqlUsuarioRoles = @"
                SELECT u.id_usuario, u.usuario, u.correo, r.nombre_rol
                FROM usuarios_anticipo u
                JOIN usuario_rol_anticipo ur ON u.id_usuario = ur.id_usuario
                JOIN roles_anticipos r ON ur.id_rol = r.id_rol
                WHERE TRIM(u.clave) = ?";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();
                Log.Information("[SSO] Conexión a BD exitosa");

                string cedula;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sqlCode;
                    cmd.Parameters.Add(new DB2Parameter { Value = code });
                    cedula = (await cmd.ExecuteScalarAsync())?.ToString()?.Trim();
                }

                if (string.IsNullOrWhiteSpace(cedula))
                {
                    Log.Warning("[SSO] Code no encontrado, ya consumido, o expirado. Code: {Code}", code);
                    return (false, new List<string>(), null, null, null);
                }

                Log.Information("[SSO] Cédula encontrada: {Cedula}", cedula);

                using var cmdUsuario = conn.CreateCommand();
                cmdUsuario.CommandText = sqlUsuarioRoles;
                cmdUsuario.Parameters.Add(new DB2Parameter { Value = cedula });

                using var reader = await cmdUsuario.ExecuteReaderAsync();

                var roles = new List<string>();
                int? idUsuario = null;
                string usuario = null;
                string correoDb = null;

                while (await reader.ReadAsync())
                {
                    idUsuario = Convert.ToInt32(reader["id_usuario"]);
                    usuario = reader["usuario"]?.ToString()?.Trim();
                    correoDb = reader["correo"]?.ToString()?.Trim();
                    var rol = reader["nombre_rol"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(rol))
                        roles.Add(char.ToUpperInvariant(rol[0]) + rol.Substring(1).ToLowerInvariant());
                }

                if (!idUsuario.HasValue || roles.Count == 0)
                {
                    Log.Warning("[SSO] Usuario no encontrado o sin roles. Clave: {Clave}, IdUsuario: {IdUsuario}, Roles: {RolesCount}",
                        cedula, idUsuario, roles.Count);
                    return (false, new List<string>(), null, null, null);
                }

                Log.Information("[SSO] Login exitoso. Usuario: {Usuario}, Roles: {Roles}", usuario, string.Join(", ", roles));
                return (true, roles, idUsuario, usuario, correoDb);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SSO] Error al validar código SSO");
                return (false, new List<string>(), null, null, null);
            }
        }

        public async Task<(bool IsRegistered, string Message, int? IdUsuario, string? Usuario, string? Correo, List<string> Roles)>
            RegistrarUsuarioAsync(string? usuario, string? correo, string? password, string? area)
        {
            usuario = usuario?.Trim();
            correo = correo?.Trim();
            password = password?.Trim();
            area = area?.Trim();

            if (string.IsNullOrWhiteSpace(usuario))
                return (false, "El usuario es obligatorio.", null, null, null, new List<string>());

            if (string.IsNullOrWhiteSpace(correo) || !correo.ToLowerInvariant().EndsWith("@recamier.com"))
                return (false, "El correo debe ser corporativo (@recamier.com).", null, null, null, new List<string>());

            if (string.IsNullOrWhiteSpace(password))
                return (false, "La contraseña es obligatoria.", null, null, null, new List<string>());

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                const string sqlExiste = @"
                    SELECT FIRST 1 id_usuario
                    FROM usuarios_anticipo
                    WHERE LOWER(TRIM(correo)) = LOWER(TRIM(@Correo))";

                await using (var cmdExiste = conn.CreateCommand())
                {
                    cmdExiste.CommandText = sqlExiste;
                    cmdExiste.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });
                    var existente = await cmdExiste.ExecuteScalarAsync();
                    if (existente != null && existente != DBNull.Value)
                        return (false, "Ya existe un usuario registrado con ese correo.", null, null, null, new List<string>());
                }

                using var tx = conn.BeginTransaction();

                const int idRol = 2;
                var nombreRol = await ObtenerNombreRolPorIdAsync(conn, tx, idRol);
                if (string.IsNullOrWhiteSpace(nombreRol))
                {
                    tx.Rollback();
                    return (false, "No existe el rol 2 configurado en roles_anticipos.", null, null, null, new List<string>());
                }

                const string sqlInsertUsuario = @"
                    INSERT INTO usuarios_anticipo (usuario, correo, clave, id_rol)
                    VALUES (@Usuario, @Correo, @Clave, @IdRol)";

                await using (var cmdInsert = conn.CreateCommand())
                {
                    cmdInsert.Transaction = tx;
                    cmdInsert.CommandText = sqlInsertUsuario;
                    cmdInsert.Parameters.Add(new DB2Parameter("@Usuario", DB2Type.VarChar) { Value = usuario });
                    cmdInsert.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });
                    cmdInsert.Parameters.Add(new DB2Parameter("@Clave", DB2Type.VarChar) { Value = password });
                    cmdInsert.Parameters.Add(new DB2Parameter("@IdRol", DB2Type.Integer) { Value = idRol });
                    await cmdInsert.ExecuteNonQueryAsync();
                }

                const string sqlIdentity = "SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1";
                int idUsuario;
                await using (var cmdIdentity = conn.CreateCommand())
                {
                    cmdIdentity.Transaction = tx;
                    cmdIdentity.CommandText = sqlIdentity;
                    var identityObj = await cmdIdentity.ExecuteScalarAsync();
                    if (identityObj == null || identityObj == DBNull.Value)
                    {
                        tx.Rollback();
                        return (false, "No fue posible obtener el ID del usuario creado.", null, null, null, new List<string>());
                    }

                    idUsuario = Convert.ToInt32(identityObj);
                }

                const string sqlInsertUsuarioRol = @"
                    INSERT INTO usuario_rol_anticipo (id_usuario, id_rol)
                    SELECT @IdUsuario, @IdRol
                    FROM systables
                    WHERE tabid = 1
                      AND NOT EXISTS (
                        SELECT 1
                        FROM usuario_rol_anticipo
                        WHERE id_usuario = @IdUsuario
                          AND id_rol = @IdRol
                    )";

                await using (var cmdInsertRel = conn.CreateCommand())
                {
                    cmdInsertRel.Transaction = tx;
                    cmdInsertRel.CommandText = sqlInsertUsuarioRol;
                    cmdInsertRel.Parameters.Add(new DB2Parameter("@IdUsuario", DB2Type.Integer) { Value = idUsuario });
                    cmdInsertRel.Parameters.Add(new DB2Parameter("@IdRol", DB2Type.Integer) { Value = idRol });
                    await cmdInsertRel.ExecuteNonQueryAsync();
                }

                tx.Commit();

                return (
                    true,
                    "Usuario registrado exitosamente.",
                    idUsuario,
                    usuario,
                    correo,
                    new List<string> { NormalizarNombreRol(nombreRol) }
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al registrar usuario con correo: {Correo}", correo);
                return (false, "No fue posible registrar el usuario.", null, null, null, new List<string>());
            }
        }

        private static string NormalizarNombreRol(string nombreRol)
        {
            if (string.IsNullOrWhiteSpace(nombreRol))
                return "Usuario";

            nombreRol = nombreRol.Trim();
            return char.ToUpperInvariant(nombreRol[0]) + nombreRol.Substring(1).ToLowerInvariant();
        }

        private static async Task<string> ObtenerNombreRolPorIdAsync(DB2Connection conn, DB2Transaction tx, int idRol)
        {
            const string sql = @"
                SELECT FIRST 1 TRIM(nombre_rol) AS nombre_rol
                FROM roles_anticipos
                WHERE id_rol = @IdRol";

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdRol", DB2Type.Integer) { Value = idRol });
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
                return reader["nombre_rol"]?.ToString()?.Trim() ?? string.Empty;

            return string.Empty;
        }
    }
}
