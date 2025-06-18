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
                id_solicitante,
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
                @IdSolicitante,
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

            cmd.Parameters.Add(new DB2Parameter("@IdSolicitante", DB2Type.Integer) { Value = dto.IdSolicitante });
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
            SET estado = 'VALIDANDO RETENCION',
            aprop_vp = 'APROBADO',
            fecha_aprobacion = CURRENT
            WHERE id_anticipo = @IdAnticipo";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = idAnticipo });

            var result = await cmd.ExecuteNonQueryAsync();
            return result > 0;
        }
        public async Task<List<SolicitudAnticipo>> ConsultarAnticiposAsync(int idUsuario)
        {
            var lista = new List<SolicitudAnticipo>();

            // Obtener el rol del usuario
            int idRol = await ObtenerRolUsuarioAsync(idUsuario);

            // Construir la consulta SQL
            var sql = @"
            SELECT 
                id_anticipo,
                id_solicitante,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                valor_a_pagar,
                pagado,
                soporte_nombre,
                soporte_pago_nombre, 
                fecha_solicitud,
                fecha_aprobacion,
                estado,
                aprop_vp,
                vp,
                tiene_legalizacion,
                quien_legaliza,
                retencion_fuente,
                retencion_iva,
                retencion_ica,
                motivo_rechazo,
                detalle_motivo_rechazo,
                otras_deducciones
            FROM anticipos_solicitados
            /**WHERE_CLAUSE**/
            ORDER BY fecha_solicitud DESC";

            // Definir la cláusula WHERE solo si no es rol 7
            string whereClause = (idRol == 1 || idRol == 7) ? "" : "WHERE id_solicitante = @IdUsuario";
            sql = sql.Replace("/**WHERE_CLAUSE**/", whereClause);

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            if (!(idRol == 1 || idRol == 7))
                cmd.Parameters.Add(new DB2Parameter("@IdUsuario", DB2Type.Integer) { Value = idUsuario });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var anticipo = new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    IdSolicitante = reader.GetInt32(1),
                    Solicitante = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    AprobadorId = reader.GetInt32(3),
                    CorreoAprobador = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    Proveedor = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    NitProveedor = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    Concepto = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    ValorAnticipo = reader.GetDecimal(8),
                    ValorAPagar = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                    Pagado = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10),
                    SoporteNombre = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    SoportePagoNombre = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                    FechaSolicitud = reader.GetDateTime(13),
                    FechaAprobacion = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14),
                    Estado = reader.IsDBNull(15) ? null : reader.GetString(15).Trim(),
                    ApropVP = reader.IsDBNull(16) ? null : reader.GetString(16).Trim(),
                    VP = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                    TieneLegalizacion = reader.IsDBNull(18) ? null : reader.GetString(18).Trim(),
                    QuienLegaliza = reader.IsDBNull(19) ? null : reader.GetString(19).Trim(),
                    RetencionFuente = reader.IsDBNull(20) ? (decimal?)null : reader.GetDecimal(20),
                    RetencionIva = reader.IsDBNull(21) ? (decimal?)null : reader.GetDecimal(21),
                    RetencionIca = reader.IsDBNull(22) ? (decimal?)null : reader.GetDecimal(22),
                    MotivoRechazo = reader.IsDBNull(23) ? null : reader.GetString(23).Trim(),
                    DetalleMotivoRechazo = reader.IsDBNull(24) ? null : reader.GetString(24).Trim(),
                    OtrasDeducciones = reader.IsDBNull(25) ? (decimal?)null : reader.GetDecimal(25)
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
                retencion_ica,
                fecha_aprobacion,
                motivo_rechazo,
                detalle_motivo_rechazo,
                otras_deducciones
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
                    RetencionIca = reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                    FechaAprobacion = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                    MotivoRechazo = reader.IsDBNull(20) ? null : reader.GetString(20).Trim(),
                    DetalleMotivoRechazo = reader.IsDBNull(21) ? null : reader.GetString(21).Trim(),
                    OtrasDeducciones = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
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
                otras_deducciones = @OtrasDeducciones,
                motivo_rechazo = @MotivoRechazo,
                detalle_motivo_rechazo = @DetalleMotivoRechazo,
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
            cmd.Parameters.Add(new DB2Parameter("@OtrasDeducciones", DB2Type.Decimal) { Value = solicitud.OtrasDeducciones ?? 0 });
            cmd.Parameters.Add(new DB2Parameter("@MotivoRechazo", DB2Type.VarChar) { Value = solicitud.MotivoRechazo ?? "" });
            cmd.Parameters.Add(new DB2Parameter("@DetalleMotivoRechazo", DB2Type.VarChar) { Value = solicitud.DetalleMotivoRechazo ?? "" });
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
                    AprobadorId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    CorreoAprobador = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                    Proveedor = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    NitProveedor = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    Concepto = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    ValorAnticipo = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                    Pagado = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                    SoporteNombre = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    FechaSolicitud = reader.GetDateTime(10),
                    Estado = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    TieneLegalizacion = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                    QuienLegaliza = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                    MotivoRechazo = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                    ValorAPagar = reader.IsDBNull(15) ? 0m : reader.GetDecimal(15),
                });
            }

            return lista;
        }
        public async Task<bool> RegistrarPagoAsync(int idAnticipo, bool pagado, string? soportePagoNombre)
        {
            string sql;
            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();

            if (!string.IsNullOrEmpty(soportePagoNombre))
            {
                sql = @"
                UPDATE anticipos_solicitados
                SET pagado = @Pagado,
                    estado = CASE WHEN @Pagado = 1 THEN 'PAGADO / PENDIENTE POR LEGALIZAR' ELSE estado END,
                    soporte_pago_nombre = @SoportePagoNombre
                WHERE id_anticipo = @IdAnticipo
            ";
                    cmd.Parameters.Add(new DB2Parameter("@SoportePagoNombre", DB2Type.VarChar) { Value = soportePagoNombre });
                }
                else
                {
                    sql = @"
                UPDATE anticipos_solicitados
                SET pagado = @Pagado,
                    estado = CASE WHEN @Pagado = 1 THEN 'PAGADO / PENDIENTE POR LEGALIZAR' ELSE estado END
                WHERE id_anticipo = @IdAnticipo
            ";
                    // NO se agrega parámetro para soporte_pago_nombre
                }

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
        public async Task<bool> RegistrarLegalizacionAsync(int idAnticipo, bool legalizado, string quienLegaliza)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET 
                tiene_legalizacion = @Legalizado,
                estado = CASE WHEN @Legalizado = 1 THEN 'FINALIZADO' ELSE estado END,
                quien_legaliza = @QuienLegaliza
            WHERE id_anticipo = @IdAnticipo
            ";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@Legalizado", DB2Type.SmallInt) { Value = legalizado ? 1 : 0 });
            cmd.Parameters.Add(new DB2Parameter("@QuienLegaliza", DB2Type.Char) { Value = quienLegaliza ?? "" });
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
                a.id_anticipo,
                a.solicitante,
                a.aprobador_id,
                a.correo_aprobador,
                a.proveedor,
                a.nit_proveedor,
                a.concepto,
                a.valor_anticipo,
                a.valor_a_pagar,
                a.pagado,
                a.soporte_nombre,
                a.soporte_pago_nombre,
                a.fecha_solicitud,
                a.fecha_aprobacion,
                a.estado,
                a.aprop_vp,
                a.vp,
                a.tiene_legalizacion,
                a.quien_legaliza,
                a.retencion_fuente,
                a.retencion_iva,
                a.retencion_ica,
                a.motivo_rechazo,
                u.correo AS correo_solicitante
            FROM anticipos_solicitados a
            JOIN usuarios_anticipo u ON a.id_solicitante = u.id_usuario
            WHERE a.id_anticipo = @IdAnticipo";

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
                    CorreoAprobador = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                    Proveedor = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    NitProveedor = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    Concepto = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    ValorAnticipo = reader.GetDecimal(7),
                    ValorAPagar = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                    Pagado = reader.IsDBNull(9) ? (decimal?)null : reader.GetDecimal(9),
                    SoporteNombre = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                    SoportePagoNombre = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    FechaSolicitud = reader.GetDateTime(12),
                    FechaAprobacion = reader.IsDBNull(13) ? (DateTime?)null : reader.GetDateTime(13),
                    Estado = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                    ApropVP = reader.IsDBNull(15) ? null : reader.GetString(15).Trim(),
                    VP = reader.IsDBNull(16) ? null : reader.GetString(16).Trim(),
                    TieneLegalizacion = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                    QuienLegaliza = reader.IsDBNull(18) ? null : reader.GetString(18).Trim(),
                    RetencionFuente = reader.IsDBNull(19) ? (decimal?)null : reader.GetDecimal(19),
                    RetencionIva = reader.IsDBNull(20) ? (decimal?)null : reader.GetDecimal(20),
                    RetencionIca = reader.IsDBNull(21) ? (decimal?)null : reader.GetDecimal(21),
                    MotivoRechazo = reader.IsDBNull(22) ? null : reader.GetString(22).Trim(),
                    CorreoSolicitante = reader.IsDBNull(23) ? null : reader.GetString(23).Trim()
                };
            }

            throw new Exception($"No se encontró el anticipo con ID {id}");
        }
        public async Task<string> ObtenerCorreoPorRolAsync(string nombreRol)
        {
            const string sql = @"
            SELECT correo FROM usuarios_anticipo u
            JOIN roles_anticipos r ON u.id_rol = r.id_rol
            WHERE LOWER(TRIM(r.nombre_rol)) = LOWER(@NombreRol)
            LIMIT 1";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@NombreRol", DB2Type.VarChar) { Value = nombreRol });

            var correo = await cmd.ExecuteScalarAsync();
            return correo?.ToString();
        }
        public async Task<bool> RechazarAnticipoAsync(int idAnticipo)
        {
            const string sql = @"
            UPDATE anticipos_solicitados
            SET estado = 'RECHAZADO',
                aprop_vp = 'RECHAZADO',
                fecha_aprobacion = CURRENT,
                motivo_rechazo = 'Anticipo Activo'
            WHERE id_anticipo = @IdAnticipo";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdAnticipo", DB2Type.Integer) { Value = idAnticipo });

            var result = await cmd.ExecuteNonQueryAsync();
            return result > 0;
        }
        public async Task<List<SolicitudAnticipo>> ConsultarAnticiposAprobadosPorCorreoAsync(string correoAprobador)
        {
            var lista = new List<SolicitudAnticipo>();

            const string sql = @"
            SELECT 
                id_anticipo,
                id_solicitante,
                solicitante,
                aprobador_id,
                correo_aprobador,
                proveedor,
                nit_proveedor,
                concepto,
                valor_anticipo,
                valor_a_pagar,
                pagado,
                soporte_nombre,
                soporte_pago_nombre,
                fecha_solicitud,
                fecha_aprobacion,
                estado,
                aprop_vp,
                vp,
                tiene_legalizacion,
                quien_legaliza,
                retencion_fuente,
                retencion_iva,
                retencion_ica,
                motivo_rechazo
            FROM anticipos_solicitados
            WHERE TRIM(LOWER(correo_aprobador)) = TRIM(LOWER(@CorreoAprobador))
              AND TRIM(aprop_vp) = 'APROBADO'
            ORDER BY fecha_aprobacion DESC";

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@CorreoAprobador", DB2Type.VarChar) { Value = correoAprobador });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SolicitudAnticipo
                {
                    IdAnticipo = reader.GetInt32(0),
                    IdSolicitante = reader.GetInt32(1),
                    Solicitante = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    AprobadorId = reader.GetInt32(3),
                    CorreoAprobador = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    Proveedor = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    NitProveedor = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    Concepto = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    ValorAnticipo = reader.GetDecimal(8),
                    ValorAPagar = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                    Pagado = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10),
                    SoporteNombre = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    SoportePagoNombre = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                    FechaSolicitud = reader.GetDateTime(13),
                    FechaAprobacion = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14),
                    Estado = reader.IsDBNull(15) ? null : reader.GetString(15).Trim(),
                    ApropVP = reader.IsDBNull(16) ? null : reader.GetString(16).Trim(),
                    VP = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                    TieneLegalizacion = reader.IsDBNull(18) ? null : reader.GetString(18).Trim(),
                    QuienLegaliza = reader.IsDBNull(19) ? null : reader.GetString(19).Trim(),
                    RetencionFuente = reader.IsDBNull(20) ? (decimal?)null : reader.GetDecimal(20),
                    RetencionIva = reader.IsDBNull(21) ? (decimal?)null : reader.GetDecimal(21),
                    RetencionIca = reader.IsDBNull(22) ? (decimal?)null : reader.GetDecimal(22),
                    MotivoRechazo = reader.IsDBNull(23) ? null : reader.GetString(23).Trim()
                });
            }

            return lista;
        }
        public async Task<int> ObtenerRolUsuarioAsync(int idUsuario)
        {
            const string sql = "SELECT id_rol FROM usuarios_anticipo WHERE id_usuario = @IdUsuario";
            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DB2Parameter("@IdUsuario", DB2Type.Integer) { Value = idUsuario });
            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
    }
}
