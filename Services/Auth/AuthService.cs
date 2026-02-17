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
    }
}
