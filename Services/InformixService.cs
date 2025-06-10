using BackendAnticipos.Models;
using IBM.Data.Db2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Threading.Tasks;

namespace BackendAnticipos.Services
{
    public class InformixService
    {
        private readonly string _connectionString;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public InformixService(IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
        {
            Console.WriteLine($"Entorno actual: {env.EnvironmentName}");

            _connectionString = env.IsDevelopment()
                ? configuration.GetConnectionString("InformixConnection")
                : configuration.GetConnectionString("InformixConnectionProduction");

            _httpContextAccessor = httpContextAccessor;

            Console.WriteLine($"Cadena seleccionada: {_connectionString}");
        }
        public bool TestConnection()
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                connection.Open();

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al conectar a la base de datos: {ex.Message}");
                return false;
            }
        }
        public async Task<int> InsertarSolicitudAnticipoAsync(SolicitudAnticipo dto)
        {
            const string sql = @"
            INSERT INTO anticipos_solicitados (
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                soporte_nombre,
                vp
            ) VALUES (
                @Solicitante,
                @AprobadorId,
                @CorreoAprobador,
                @Proveedor,
                @NitProveedor,
                @Concepto,
                @ValorAnticipo,
                @SoporteNombre,
                @VP
            )";

            const string identitySql =
            "SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1";

            await using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.Add(new DB2Parameter("@Solicitante", DB2Type.VarChar) { Value = dto.Solicitante });
            cmd.Parameters.Add(new DB2Parameter("@AprobadorId", DB2Type.Integer) { Value = dto.AprobadorId });
            cmd.Parameters.Add(new DB2Parameter("@CorreoAprobador", DB2Type.VarChar) { Value = dto.CorreoAprobador });
            cmd.Parameters.Add(new DB2Parameter("@Proveedor", DB2Type.VarChar) { Value = dto.Proveedor });
            cmd.Parameters.Add(new DB2Parameter("@NitProveedor", DB2Type.VarChar) { Value = dto.NitProveedor });
            cmd.Parameters.Add(new DB2Parameter("@Concepto", DB2Type.VarChar) { Value = dto.Concepto });
            cmd.Parameters.Add(new DB2Parameter("@ValorAnticipo", DB2Type.Decimal) { Value = dto.ValorAnticipo, Precision = 16, Scale = 2 });
            cmd.Parameters.Add(new DB2Parameter("@SoporteNombre", DB2Type.VarChar) { Value = (object?)dto.SoporteNombre ?? DBNull.Value });
            cmd.Parameters.Add(new DB2Parameter("@VP", DB2Type.VarChar) { Value = dto.VP });

            await cmd.ExecuteNonQueryAsync();

            await using var identityCmd = conn.CreateCommand();
            identityCmd.CommandText = identitySql;

            var result = await identityCmd.ExecuteScalarAsync();

            return Convert.ToInt32(result);

        }
        public async Task<bool> AprobarAnticipoAsync(int idAnticipo)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET estado = 'VALIDANDO RETENCION'
            WHERE id_anticipo = @IdAnticipo";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = idAnticipo });

            var result = await cmd.ExecuteNonQueryAsync();
            return result > 0;
        }
        public async Task<List<SolicitudAnticipo>> ConsultarAnticiposAsync()
        {
            var lista = new List<SolicitudAnticipo>();

            const string sql = @"
            SELECT 
                id_anticipo,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                soporte_nombre,
                fecha_solicitud,
                estado
            FROM anticipos_solicitados
            ORDER BY fecha_solicitud DESC";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var anticipo = new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    Solicitante = reader.GetString(1).Trim(),
                    AprobadorId = reader.GetInt32(2),
                    CorreoAprobador = reader.GetString(3).Trim(),
                    Proveedor = reader.GetString(4).Trim(),
                    NitProveedor = reader.GetString(5).Trim(),
                    Concepto = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    ValorAnticipo = reader.GetDecimal(7),
                    SoporteNombre = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    FechaSolicitud = reader.GetDateTime(9),
                    Estado = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                };

                lista.Add(anticipo);
            }


            return lista;
        }
        public async Task<List<Proveedor>> BuscarProveedoresAsync(string? filtro, int page = 1, int pageSize = 50)
        {
            var lista = new List<Proveedor>();
            var filtros = new List<string> { "vm.vm_cmpy = 'RE'" };
            var parametros = new List<DB2Parameter>();

            string baseSql = @"
            SELECT vm.vm_vend, vm.vm_name, vm.vm_tele
            FROM vendmain vm";

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtros.Add("(TRIM(vm.vm_vend) = @FiltroExacto OR LOWER(TRIM(vm.vm_name)) LIKE LOWER(@FiltroParcial))");
                parametros.Add(new DB2Parameter("@FiltroExacto", DB2Type.VarChar) { Value = filtro.Trim() });
                parametros.Add(new DB2Parameter("@FiltroParcial", DB2Type.VarChar) { Value = $"%{filtro.Trim()}%" });
            }

            if (filtros.Count > 0)
                baseSql += " WHERE " + string.Join(" AND ", filtros);

            baseSql += " ORDER BY vm.vm_name";

            // Agregar paginación (Informix usa SKIP/FIRST)
            int offset = (page - 1) * pageSize;
            baseSql += $" SKIP {offset} FIRST {pageSize}";

            try
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = baseSql;

                foreach (var param in parametros)
                    cmd.Parameters.Add(param);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lista.Add(new Proveedor
                    {
                        Codigo = reader["vm_vend"]?.ToString().Trim(),
                        Nombre = reader["vm_name"]?.ToString().Trim(),
                        Telefono = reader["vm_tele"] == DBNull.Value ? null : reader["vm_tele"].ToString().Trim()
                    });
                }

                return lista;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al consultar proveedores: {ex.Message}\n{ex.StackTrace}");
                return new List<Proveedor>();
            }
        }
        public async Task<List<SolicitudAnticipo>> ConsultarRetencionAsync()
        {
            var lista = new List<SolicitudAnticipo>();

            const string sql = @"
            SELECT
                id_anticipo,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                pagado,
                soporte_nombre,
                fecha_solicitud,
                estado,
                aprop_vp,
                vp,
                tiene_legalizacion,
                quien_legaliza,
                retencion_fuente,
                retencion_iva,
                retencion_ica
            FROM anticipos_solicitados
            WHERE TRIM(estado) = 'VALIDANDO RETENCION'
            ORDER BY fecha_solicitud DESC";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    Solicitante = reader.GetString(1).Trim(),
                    AprobadorId = reader.GetInt32(2),
                    CorreoAprobador = reader.GetString(3).Trim(),
                    Proveedor = reader.GetString(4).Trim(),
                    NitProveedor = reader.GetString(5).Trim(),
                    Concepto = reader.GetString(6).Trim(),
                    ValorAnticipo = reader.GetDecimal(7),
                    Pagado = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    SoporteNombre = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    FechaSolicitud = reader.GetDateTime(10),
                    Estado = reader.GetString(11).Trim(),
                    ApropVP = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                    VP = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                    TieneLegalizacion = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                    QuienLegaliza = reader.IsDBNull(15) ? null : reader.GetString(15).Trim(),
                    RetencionFuente = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                    RetencionIva = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    RetencionIca = reader.IsDBNull(18) ? null : reader.GetDecimal(18)
                });
            }

            return lista;
        }
        public async Task<bool> ActualizarRetencionAsync(ActualizarRetencionDTO solicitud)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET
                retencion_fuente = @RetencionFuente,
                retencion_iva = @RetencionIva,
                retencion_ica = @RetencionIca,
                motivo_rechazo = @MotivoRechazo,
                valor_a_pagar = @ValorAPagar,
                estado = @Estado
            WHERE id_anticipo = @IdAnticipo
            ";

            var estado = solicitud.MotivoRechazo?.Trim() == "Anticipo Activo"
            ? "PENDIENTE DE PAGO"
            : "FINALIZADO";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.Add(new DB2Parameter("@RetencionFuente", DB2Type.Decimal) { Value = solicitud.RetencionFuente ?? 0 });
            cmd.Parameters.Add(new DB2Parameter("@RetencionIva", DB2Type.Decimal) { Value = solicitud.RetencionIva ?? 0 });
            cmd.Parameters.Add(new DB2Parameter("@RetencionIca", DB2Type.Decimal) { Value = solicitud.RetencionIca ?? 0 });
            cmd.Parameters.Add(new DB2Parameter("@MotivoRechazo", DB2Type.VarChar) { Value = solicitud.MotivoRechazo ?? "" });
            cmd.Parameters.Add(new DB2Parameter("@ValorAPagar", DB2Type.Decimal) { Value = solicitud.ValorAPagar ?? 0 });
            cmd.Parameters.Add(new DB2Parameter("@Estado", DB2Type.VarChar) { Value = estado });
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = solicitud.IdAnticipo });

            var result = await cmd.ExecuteNonQueryAsync();

            return result > 0;
        }
        public async Task<List<SolicitudAnticipo>> ConsultarAnticiposParaPagoAsync()
        {
            var lista = new List<SolicitudAnticipo>();

            const string sql = @"
            SELECT
                id_anticipo,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                pagado,
                soporte_nombre,
                fecha_solicitud,
                estado,
                tiene_legalizacion,
                quien_legaliza,
                motivo_rechazo,
                valor_a_pagar
            FROM anticipos_solicitados
            WHERE TRIM(estado) = 'PENDIENTE DE PAGO'
            ORDER BY fecha_solicitud DESC
        ";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    Solicitante = reader.GetString(1).Trim(),
                    AprobadorId = reader.GetInt32(2),
                    CorreoAprobador = reader.GetString(3).Trim(),
                    Proveedor = reader.GetString(4).Trim(),
                    NitProveedor = reader.GetString(5).Trim(),
                    Concepto = reader.GetString(6).Trim(),
                    ValorAnticipo = reader.GetDecimal(7),
                    Pagado = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    SoporteNombre = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    FechaSolicitud = reader.GetDateTime(10),
                    Estado = reader.GetString(11).Trim(),
                    TieneLegalizacion = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                    QuienLegaliza = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                    MotivoRechazo = reader.GetString(14).Trim(),
                    ValorAPagar = reader.GetDecimal(15),
                });
            }

            return lista;
        }
        public async Task<bool> RegistrarPagoAsync(int idAnticipo, bool pagado)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET pagado = @Pagado,
                estado = CASE WHEN @Pagado = 1 THEN 'PAGADO / PENDIENTE POR LEGALIZAR' ELSE estado END
            WHERE id_anticipo = @IdAnticipo
            ";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@Pagado", DB2Type.SmallInt) { Value = pagado ? 1 : 0 });
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = idAnticipo });

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        public async Task<List<SolicitudAnticipo>> ConsultarLegalizarAnticiposAsync()
        {
            var lista = new List<SolicitudAnticipo>();

            const string sql = @"
            SELECT
                id_anticipo,
                solicitante,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                valor_a_pagar,
                fecha_solicitud,
                estado,
                tiene_legalizacion,
                retencion_fuente,
                retencion_iva,
                retencion_ica
            FROM anticipos_solicitados
            WHERE TRIM(estado) = 'PAGADO / PENDIENTE POR LEGALIZAR'
            ORDER BY fecha_solicitud DESC";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    Solicitante = reader.GetString(1).Trim(),
                    Proveedor = reader.GetString(2).Trim(),
                    NitProveedor = reader.GetString(3).Trim(),
                    Concepto = reader.GetString(4).Trim(),
                    ValorAnticipo = reader.GetDecimal(5),
                    ValorAPagar = reader.GetDecimal(6),
                    FechaSolicitud = reader.GetDateTime(7),
                    Estado = reader.GetString(8).Trim(),
                    TieneLegalizacion = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    RetencionFuente = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                    RetencionIva = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                    RetencionIca = reader.IsDBNull(12) ? null : reader.GetDecimal(12)
                });
            }

            return lista;
        }
        public async Task<bool> RegistrarLegalizacionAsync(int idAnticipo, bool legalizado)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET 
                tiene_legalizacion = @Legalizado,
                estado = CASE WHEN @Legalizado = 1 THEN 'FINALIZADO' ELSE estado END
            WHERE id_anticipo = @IdAnticipo
            ";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@Legalizado", DB2Type.SmallInt) { Value = legalizado ? 1 : 0 });
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = idAnticipo });

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();
            return rowsAffected > 0;
        }
        public async Task<List<object>> ObtenerTotalPagadoPorMesAsync(int? anio = null, int? mes = null)
        {
            const string sql = @"
            SELECT TO_CHAR(fecha_solicitud, '%Y-%m') AS mes,
                   SUM(valor_a_pagar) AS total_pagado
            FROM anticipos_solicitados
            {WHERE_CLAUSE}
            GROUP BY 1
            ORDER BY 1";
            return await EjecutarConsultaEstadistica(sql, anio, mes);
        }
        public async Task<List<object>> ObtenerCantidadAnticiposPorMesAsync(int? anio = null, int? mes = null)
        {
            const string sql = @"
            SELECT TO_CHAR(fecha_solicitud, '%Y-%m') AS mes,
                   COUNT(*) AS total_anticipos
            FROM anticipos_solicitados
            {WHERE_CLAUSE}
            GROUP BY 1
            ORDER BY 1";
            return await EjecutarConsultaEstadistica(sql, anio, mes);
        }
        public async Task<List<object>> ObtenerSolicitadoVsPagadoPorMesAsync(int? anio = null, int? mes = null)
        {
            const string sql = @"
            SELECT TO_CHAR(fecha_solicitud, '%Y-%m') AS mes,
                   SUM(valor_anticipo) AS total_solicitado,
                   SUM(valor_a_pagar) AS total_pagado
            FROM anticipos_solicitados
            {WHERE_CLAUSE}
            GROUP BY 1
            ORDER BY 1";
            return await EjecutarConsultaEstadistica(sql, anio, mes);
        }
        private async Task<List<object>> EjecutarConsultaEstadistica(string sqlBase, int? anio, int? mes)
        {
            var resultados = new List<object>();
            var parametros = new List<DB2Parameter>();
            var where = string.Empty;

            if (anio.HasValue && mes.HasValue)
            {
                // 1 → enero, 12 → diciembre
                DateTime inicioMes = new(anio.Value, mes.Value, 1);       // 2025-04-01 00:00
                DateTime inicioSgte = inicioMes.AddMonths(1);              // 2025-05-01 00:00

                where = "WHERE fecha_solicitud >= ? AND fecha_solicitud < ?";

                // Para IBM.Data.Db2  se recomienda DB2Type.Timestamp
                parametros.Add(new DB2Parameter { DB2Type = DB2Type.Timestamp, Value = inicioMes });
                parametros.Add(new DB2Parameter { DB2Type = DB2Type.Timestamp, Value = inicioSgte });
            }

            string sql = sqlBase.Replace("{WHERE_CLAUSE}", where);

            await using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parametros) cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resultados.Add(
                    Enumerable.Range(0, reader.FieldCount)
                              .ToDictionary(i => reader.GetName(i),
                                            i => reader.IsDBNull(i) ? null : reader.GetValue(i)));
            }
            return resultados;
        }
        public async Task<SolicitudAnticipo> ConsultarSolicitudAnticipoPorIdAsync(int id)
        {
            const string sql = @"
            SELECT 
                id_anticipo,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                soporte_nombre,
                fecha_solicitud,
                estado,
                vp
            FROM anticipos_solicitados
            WHERE id_anticipo = @IdAnticipo";

            await using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = id });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    Solicitante = reader.GetString(1).Trim(),
                    AprobadorId = reader.GetInt32(2),
                    CorreoAprobador = reader.GetString(3).Trim(),
                    Proveedor = reader.GetString(4).Trim(),
                    NitProveedor = reader.GetString(5).Trim(),
                    Concepto = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    ValorAnticipo = reader.GetDecimal(7),
                    SoporteNombre = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    FechaSolicitud = reader.GetDateTime(9),
                    Estado = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                    VP = reader.IsDBNull(11) ? null : reader.GetString(11).Trim()
                };
            }

            throw new Exception($"No se encontró el anticipo con ID {id}");
        }

    }
}
