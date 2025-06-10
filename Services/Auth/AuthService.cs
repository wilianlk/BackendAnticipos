using IBM.Data.Db2;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System;
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

        // Método para validar login (usuario y rol)
        public async Task<(bool IsValid, string Rol, int? IdUsuario)> ValidarUsuarioAsync(string username, string password)
        {
            const string sql = @"
                SELECT u.id_usuario,r.nombre_rol
                FROM usuarios_anticipo u
                JOIN roles_anticipos r ON u.id_rol = r.id_rol
                WHERE TRIM(u.usuario) = @Username AND TRIM(u.clave) = @Password";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.Add(new DB2Parameter("@Username", DB2Type.VarChar) { Value = username });
                cmd.Parameters.Add(new DB2Parameter("@Password", DB2Type.VarChar) { Value = password });

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var idUsuario = Convert.ToInt32(reader["id_usuario"]);
                    var rol = reader["nombre_rol"]?.ToString().Trim();

                    if (!string.IsNullOrEmpty(rol))
                        rol = char.ToUpper(rol[0]) + rol.Substring(1).ToLower();

                    return (true, rol, idUsuario);
                }

                return (false, null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al validar usuario: {ex.Message}");
                return (false, null, null);
            }
        }

        // Método para registrar usuario
        public async Task<bool> RegistrarUsuarioAsync(string username, string password, string rol)
        {
            // Verificar si el usuario ya existe
            const string checkSql = @"
                SELECT COUNT(*) 
                FROM usuarios_anticipo
                WHERE TRIM(usuario) = @Username";

            // Obtener id_rol desde roles_anticipos
            const string getRolIdSql = @"
                SELECT id_rol 
                FROM roles_anticipos
                WHERE TRIM(nombre_rol) = @Rol";

            // Insertar usuario
            const string insertSql = @"
                INSERT INTO usuarios_anticipo (usuario, clave, id_rol)
                VALUES (@Username, @Password, @IdRol)";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                // Verificar si el usuario existe
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = checkSql;
                checkCmd.Parameters.Add(new DB2Parameter("@Username", DB2Type.VarChar) { Value = username });
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count > 0)
                {
                    // Usuario ya existe
                    return false;
                }

                // Obtener id_rol
                using var getRolCmd = conn.CreateCommand();
                getRolCmd.CommandText = getRolIdSql;
                getRolCmd.Parameters.Add(new DB2Parameter("@Rol", DB2Type.VarChar) { Value = rol });
                var idRolObj = await getRolCmd.ExecuteScalarAsync();
                if (idRolObj == null)
                {
                    throw new Exception("El rol especificado no existe.");
                }
                int idRol = Convert.ToInt32(idRolObj);

                // Insertar usuario
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = insertSql;
                insertCmd.Parameters.Add(new DB2Parameter("@Username", DB2Type.VarChar) { Value = username });
                insertCmd.Parameters.Add(new DB2Parameter("@Password", DB2Type.VarChar) { Value = password });
                insertCmd.Parameters.Add(new DB2Parameter("@IdRol", DB2Type.Integer) { Value = idRol });

                var result = await insertCmd.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al registrar usuario: {ex.Message}");
                return false;
            }
        }
    }
}
