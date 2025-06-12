using IBM.Data.Db2;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
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

        // LOGIN: retorna usuario y lista de roles
        public async Task<(bool IsValid, List<string> Roles, int? IdUsuario, string Usuario)> ValidarUsuarioAsync(string correo, string password)
        {
            if (string.IsNullOrWhiteSpace(correo) || !correo.Trim().ToLower().EndsWith("@recamier.com"))
                return (false, null, null, null);

            // Usar ? para parámetros en Informix
            const string sql = @"
        SELECT u.id_usuario, u.usuario, r.nombre_rol
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
                List<string> roles = new();
                int? idUsuario = null;
                string usuario = null;

                while (await reader.ReadAsync())
                {
                    idUsuario = Convert.ToInt32(reader["id_usuario"]);
                    usuario = reader["usuario"]?.ToString().Trim();
                    var rol = reader["nombre_rol"]?.ToString().Trim();
                    if (!string.IsNullOrEmpty(rol))
                        roles.Add(char.ToUpper(rol[0]) + rol.Substring(1).ToLower());
                }

                if (idUsuario.HasValue && roles.Count > 0)
                    return (true, roles, idUsuario, usuario);

                return (false, null, null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al validar usuario: {ex.Message}");
                return (false, null, null, null);
            }
        }

        public async Task<bool> RegistrarUsuarioAsync(string usuario, string password, List<string> roles, string correo)
        {
            if (string.IsNullOrWhiteSpace(correo) ||
                !correo.Trim().ToLower().EndsWith("@recamier.com") ||
                string.IsNullOrWhiteSpace(usuario) ||
                roles == null || roles.Count == 0)
            {
                return false;
            }

            const string checkSql = @"
                SELECT COUNT(*) 
                FROM usuarios_anticipo
                WHERE TRIM(correo) = @Correo OR TRIM(usuario) = @Usuario";

            const string insertSql = @"
                INSERT INTO usuarios_anticipo (usuario, clave, correo, area)
                VALUES (@Usuario, @Password, @Correo, @Area)";

            const string getUserIdSql = @"
                SELECT id_usuario FROM usuarios_anticipo
                WHERE TRIM(usuario) = @Usuario AND TRIM(correo) = @Correo";

            const string getRolIdSql = @"
                SELECT id_rol 
                FROM roles_anticipos
                WHERE TRIM(nombre_rol) = @Rol";

            const string insertUserRolSql = @"
                INSERT INTO usuario_rol_anticipo (id_usuario, id_rol)
                VALUES (@IdUsuario, @IdRol)";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                // Verificar si el correo o usuario existen
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = checkSql;
                checkCmd.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });
                checkCmd.Parameters.Add(new DB2Parameter("@Usuario", DB2Type.VarChar) { Value = usuario });
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count > 0)
                {
                    // Usuario o correo ya existe
                    return false;
                }

                // Insertar usuario (por defecto área "SIN_AREA" si no hay campo)
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = insertSql;
                insertCmd.Parameters.Add(new DB2Parameter("@Usuario", DB2Type.VarChar) { Value = usuario });
                insertCmd.Parameters.Add(new DB2Parameter("@Password", DB2Type.VarChar) { Value = password });
                insertCmd.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });
                insertCmd.Parameters.Add(new DB2Parameter("@Area", DB2Type.Char) { Value = "SIN_AREA" });
                var result = await insertCmd.ExecuteNonQueryAsync();
                if (result <= 0)
                    return false;

                // Obtener id_usuario recién insertado
                int idUsuario;
                using (var getUserIdCmd = conn.CreateCommand())
                {
                    getUserIdCmd.CommandText = getUserIdSql;
                    getUserIdCmd.Parameters.Add(new DB2Parameter("@Usuario", DB2Type.VarChar) { Value = usuario });
                    getUserIdCmd.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correo });
                    var idObj = await getUserIdCmd.ExecuteScalarAsync();
                    if (idObj == null)
                        return false;
                    idUsuario = Convert.ToInt32(idObj);
                }

                // Insertar los roles para este usuario
                foreach (var rol in roles)
                {
                    // Buscar id_rol
                    int idRol;
                    using (var getRolCmd = conn.CreateCommand())
                    {
                        getRolCmd.CommandText = getRolIdSql;
                        getRolCmd.Parameters.Add(new DB2Parameter("@Rol", DB2Type.VarChar) { Value = rol });
                        var idRolObj = await getRolCmd.ExecuteScalarAsync();
                        if (idRolObj == null)
                            throw new Exception($"El rol '{rol}' no existe.");
                        idRol = Convert.ToInt32(idRolObj);
                    }
                    // Insertar asociación usuario-rol
                    using (var insertUserRolCmd = conn.CreateCommand())
                    {
                        insertUserRolCmd.CommandText = insertUserRolSql;
                        insertUserRolCmd.Parameters.Add(new DB2Parameter("@IdUsuario", DB2Type.Integer) { Value = idUsuario });
                        insertUserRolCmd.Parameters.Add(new DB2Parameter("@IdRol", DB2Type.Integer) { Value = idRol });
                        await insertUserRolCmd.ExecuteNonQueryAsync();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al registrar usuario: {ex.Message}");
                return false;
            }
        }
    }
}
