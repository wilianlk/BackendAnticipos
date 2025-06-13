using System;
using BackendAnticipos.Models;
using BackendAnticipos.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.Net.Mail;
using System.Net;
using BackendAnticipos.Models.Settings;
using Microsoft.Extensions.Options;

namespace BackendAnticipos.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnticiposController : ControllerBase
    {
        private readonly InformixService _informixService;
        private readonly ILogger<AnticiposController> _logger;
        private readonly SmtpSettings _smtp;
        private readonly string _baseUrl;

        public AnticiposController(InformixService informixService, ILogger<AnticiposController> logger, IOptions<SmtpSettings> smtpOptions, IWebHostEnvironment env)
        {
            _informixService = informixService;
            _logger = logger;
            _smtp = smtpOptions.Value;

            _baseUrl = env.IsDevelopment()
            ? "http://localhost:5173"
            : "http://192.168.20.30:8089";
        }

        [HttpGet("env-check")]
        public IActionResult GetEnvironment([FromServices] IWebHostEnvironment env)
        {
            return Ok(new
            {
                Environment = env.EnvironmentName,
                IsDevelopment = env.IsDevelopment()
            });
        }

        [HttpGet("test-connection")]
        public IActionResult TestDatabaseConnection()
        {
            _logger.LogInformation("Iniciando prueba de conexión a la base de datos.");

            bool isConnected = _informixService.TestConnection();

            if (isConnected)
            {
                _logger.LogInformation("Conexión exitosa a la base de datos.");
                return Ok("Conexión exitosa a la base de datos.");
            }
            else
            {
                _logger.LogError("Error al conectar a la base de datos.");
                return StatusCode(500, "Error al conectar a la base de datos.");
            }
        }

        [HttpPost("solicitud")]
        public async Task<IActionResult> SolicitudAnticipo(
        [FromForm] SolicitudAnticipo dto,
        [FromServices] IWebHostEnvironment env)
        {
            try
            {
                if (dto == null)
                    return BadRequest(new { success = false, message = "Solicitud inválida." });

                // 1. Guardar archivo si viene
                if (dto.Soporte is { Length: > 0 })
                {
                    var ext = Path.GetExtension(dto.Soporte.FileName);
                    var fileName = $"{Guid.NewGuid()}{ext}";

                    var dir = Path.Combine(Directory.GetCurrentDirectory(), "Soportes");
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        _logger.LogInformation("Carpeta Soportes creada en: {Dir}", dir);
                    }

                    var pathFisica = Path.Combine(dir, fileName);

                    await using var fs = new FileStream(pathFisica, FileMode.Create);
                    await dto.Soporte.CopyToAsync(fs);

                    dto.SoporteNombre = Path.Combine("Soportes", fileName);
                }

                int idAnticipo = await _informixService.InsertarSolicitudAnticipoAsync(dto);

                if (idAnticipo > 0)
                {
                    _logger.LogInformation("Solicitud registrada. Soporte: {Ruta}",
                                           dto.SoporteNombre ?? "(sin archivo)");

                    dto = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(idAnticipo);

                    var destinatarios = new List<string>();
                    if (!string.IsNullOrWhiteSpace(dto.CorreoSolicitante))
                        destinatarios.Add(dto.CorreoSolicitante);

                    /*if (!string.IsNullOrWhiteSpace(dto.CorreoAprobador))
                        destinatarios.Add(dto.CorreoAprobador);*/

                    if (!string.IsNullOrWhiteSpace(dto.CorreoAprobador))
                        destinatarios.Add("powerapps@recamier.com");

                    await EnviarCorreoAsync(dto, _baseUrl, destinatarios);

                    return Ok(new
                    {
                        success = true,
                        message = "Solicitud registrada exitosamente.",
                        rutaSoporte = dto.SoporteNombre
                    });
                }

                _logger.LogWarning("La solicitud no se pudo registrar.");
                return StatusCode(500, new { success = false, message = "No se registró ningún dato." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar la solicitud.");
                return StatusCode(500, new { success = false, message = "Error interno al registrar la solicitud." });
            }
        }

        [HttpGet("buscar-proveedores")]
        public async Task<IActionResult> BuscarProveedores([FromQuery] string? filtro, [FromQuery] int page = 1,[FromQuery] int pageSize = 50)
        {
            _logger.LogInformation("Buscando proveedores...");

            try
            {
                var proveedores = await _informixService.BuscarProveedoresAsync(filtro, page, pageSize);

                if (proveedores == null || proveedores.Count == 0)
                {
                    _logger.LogWarning("No se encontraron proveedores con los criterios dados.");
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron proveedores con los criterios dados."
                    });
                }

                return Ok(new
                {
                    success = true,
                    total = proveedores.Count,
                    data = proveedores
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar proveedores.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al buscar proveedores."
                });
            }
        }

        [HttpPut("aprobar")]
        public async Task<IActionResult> AprobarORechazarAnticipo([FromQuery] int idAnticipo, [FromQuery] string accion)
        {
            _logger.LogInformation($"[DEBUG] Acción recibida: {accion} para anticipo: {idAnticipo}");

            if (idAnticipo <= 0)
                return BadRequest(new { success = false, message = "Id de anticipo inválido." });

            if (!string.IsNullOrWhiteSpace(accion) && accion.ToLower() == "rechazar")
            {
                var rechazado = await _informixService.RechazarAnticipoAsync(idAnticipo);

                if (rechazado)
                {
                    var dto = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(idAnticipo);
                    var destinatarios = new List<string>();

                    if (!string.IsNullOrWhiteSpace(dto.CorreoSolicitante))
                        destinatarios.Add(dto.CorreoSolicitante);

                    //if (!string.IsNullOrWhiteSpace(dto.CorreoAprobador))
                        //destinatarios.Add(dto.CorreoAprobador);

                    destinatarios.Add("powerapps@recamier.com");
                    /*destinatarios.Add("mcvaron@recamier.com");
                    destinatarios.Add("lvergara@recamier.com");
                    destinatarios.Add("cltapia@recamier.com");*/

                    destinatarios = destinatarios
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .ToList();

                    await EnviarCorreoAsync(dto, _baseUrl, destinatarios);

                    _logger.LogInformation($"Anticipo {idAnticipo} rechazado correctamente y correo enviado.");
                    return Ok(new { success = true, message = "Anticipo rechazado correctamente." });
                }
                else
                {
                    _logger.LogWarning($"No se encontró o no se pudo rechazar el anticipo {idAnticipo}.");
                    return NotFound(new { success = false, message = "No se encontró o no se pudo rechazar el anticipo." });
                }
            }
            else
            {
                var actualizado = await _informixService.AprobarAnticipoAsync(idAnticipo);

                if (actualizado)
                {
                    var dto = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(idAnticipo);
                    if (dto.Estado == "VALIDANDO RETENCION")
                    {
                        var correoRetenciones = await _informixService.ObtenerCorreoPorRolAsync("Retenciones");

                        var destinatarios = new List<string>();

                        if (!string.IsNullOrWhiteSpace(dto.CorreoSolicitante))
                            destinatarios.Add(dto.CorreoSolicitante);

                        //if (!string.IsNullOrWhiteSpace(correoRetenciones))
                        //    destinata
                        
                        if (!string.IsNullOrWhiteSpace(correoRetenciones))
                            destinatarios.Add(correoRetenciones);

                        /*destinatarios.Add("powerapps@recamier.com");
                        destinatarios.Add("mcvaron@recamier.com");
                        destinatarios.Add("lvergara@recamier.com");
                        destinatarios.Add("cltapia@recamier.com");*/

                        destinatarios = destinatarios
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()
                            .ToList();

                        await EnviarCorreoAsync(dto, _baseUrl, destinatarios);

                        _logger.LogInformation($"Anticipo {idAnticipo} aprobado correctamente y correo enviado.");
                    }
                    else
                    {
                        _logger.LogWarning($"Anticipo {idAnticipo} no se encuentra en estado 'VALIDANDO RETENCION'. No se envía correo.");
                    }
                    return Ok(new { success = true, message = "Anticipo aprobado correctamente." });
                }
                else
                {
                    _logger.LogWarning($"No se encontró o no se pudo aprobar el anticipo {idAnticipo}.");
                    return NotFound(new { success = false, message = "No se encontró o no se pudo aprobar el anticipo." });
                }
            }
        }

        [HttpGet("ConsultaAnticipo")]
        public async Task<IActionResult> ConsultaAnticipo([FromQuery] int idUsuario)
        {
            _logger.LogInformation("Iniciando consulta de anticipos...");

            try
            {
                var resultados = await _informixService.ConsultarAnticiposAsync(idUsuario);

                if (resultados == null || resultados.Count == 0)
                {
                    _logger.LogWarning("No se encontraron anticipos registrados.");
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron anticipos registrados."
                    });
                }

                _logger.LogInformation($"Consulta exitosa. Registros encontrados: {resultados.Count}");
                return Ok(new
                {
                    success = true,
                    total = resultados.Count,
                    data = resultados
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió un error al consultar anticipos.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al consultar anticipos."
                });
            }
        }

        [HttpGet("rev-retenciones")]
        public async Task<IActionResult> ObtenerAnticiposParaRevisionRetenciones()
        {
            _logger.LogInformation("Consultando anticipos para revisión de retenciones...");

            try
            {
                var anticipos = await _informixService.ConsultarRetencionAsync();

                if (anticipos == null || anticipos.Count == 0)
                {
                    _logger.LogWarning("No se encontraron anticipos para revisión de retenciones.");
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron anticipos para revisión de retenciones."
                    });
                }

                _logger.LogInformation($"Consulta exitosa. Registros encontrados: {anticipos.Count}");
                return Ok(new
                {
                    success = true,
                    total = anticipos.Count,
                    data = anticipos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar anticipos para revisión de retenciones.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al consultar anticipos para revisión de retenciones."
                });
            }
        }

        [HttpPost("actualizar-retencion")]
        public async Task<IActionResult> ActualizarRetencion([FromBody] ActualizarRetencionDTO solicitud)
        {
            try
            {
                if (solicitud == null)
                    return BadRequest(new { success = false, message = "Solicitud inválida." });

                var actualizado = await _informixService.ActualizarRetencionAsync(solicitud);

                if (actualizado)
                {
                    var dto = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(solicitud.IdAnticipo);

                    var destinatarios = new List<string>();

                    if (dto.Estado == "PENDIENTE DE PAGO")
                    {
                        var correoPagador = await _informixService.ObtenerCorreoPorRolAsync("Pagador");

                        if (!string.IsNullOrWhiteSpace(dto.CorreoSolicitante))
                            destinatarios.Add(dto.CorreoSolicitante);

                            destinatarios.Add("powerapps@recamier.com");
                            //destinatarios.Add("almesa@recamier.com");

                            destinatarios = destinatarios
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct()
                                .ToList();

                        await EnviarCorreoAsync(dto, _baseUrl, destinatarios);

                        _logger.LogInformation("Correo de notificación enviado para estado 'PENDIENTE DE PAGO'.");
                    }
                    else if (dto.Estado == "FINALIZADO")
                    {

                        if (!string.IsNullOrWhiteSpace(dto.CorreoSolicitante))
                            destinatarios.Add(dto.CorreoSolicitante);

                        await EnviarCorreoAsync(dto, _baseUrl, destinatarios);

                        _logger.LogInformation("Correo de notificación enviado para estado 'FINALIZADO'.");
                    }
                    else
                    {
                        _logger.LogWarning($"Anticipo {solicitud.IdAnticipo} no se encuentra en estado esperado. No se envía correo.");
                    }
                    return Ok(new { success = true, message = "Retenciones actualizadas correctamente." });
                }
                else
                {
                    _logger.LogWarning("No se pudo actualizar el registro de retenciones.");
                    return StatusCode(500, new { success = false, message = "No se pudo actualizar el registro." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar retenciones.");
                return StatusCode(500, new { success = false, message = "Error interno al actualizar retenciones." });
            }
        }

        [HttpGet("efectuar-pago")]
        public async Task<IActionResult> ObtenerAnticiposParaPago()
        {
            try
            {
                var anticipos = await _informixService.ConsultarAnticiposParaPagoAsync();

                if (anticipos == null || anticipos.Count == 0)
                {
                    _logger.LogWarning("No se encontraron anticipos pendientes de pago.");
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron anticipos pendientes de pago."
                    });
                }

                _logger.LogInformation($"Consulta exitosa. Registros encontrados: {anticipos.Count}");

                return Ok(new
                {
                    success = true,
                    total = anticipos.Count,
                    data = anticipos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar anticipos para efectuar pagos.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al consultar anticipos para efectuar pagos."
                });
            }
        }

        [HttpPost("registrar-pago")]
        public async Task<IActionResult> RegistrarPago([FromForm] RegistrarPagoFormDto dto)
        {
            try
            {
                _logger.LogInformation("Iniciando registro de pago para anticipo {IdAnticipo}", dto.IdAnticipo);

                string? soportePagoNombre = null;

                if (dto.SoportePago != null && dto.SoportePago.Length > 0)
                {
                    var ext = Path.GetExtension(dto.SoportePago.FileName);
                    var fileName = $"{Guid.NewGuid()}{ext}";

                    var dir = Path.Combine(Directory.GetCurrentDirectory(), "Soportes");
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        _logger.LogInformation("Carpeta Soportes creada en: {Dir}", dir);
                    }

                    var pathFisica = Path.Combine(dir, fileName);

                    await using var fs = new FileStream(pathFisica, FileMode.Create);
                    await dto.SoportePago.CopyToAsync(fs);
                    soportePagoNombre = Path.Combine("Soportes", fileName);

                    _logger.LogInformation("Archivo de soporte de pago guardado en {Ruta}", soportePagoNombre);
                }
                else
                {
                    _logger.LogInformation("No se recibió archivo de soporte de pago para el anticipo {IdAnticipo}", dto.IdAnticipo);
                }

                var ok = await _informixService.RegistrarPagoAsync(dto.IdAnticipo, dto.Pagado, soportePagoNombre);

                if (ok)
                {
                    _logger.LogInformation("Pago registrado exitosamente para anticipo {IdAnticipo}", dto.IdAnticipo);

                    var anticipo = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(dto.IdAnticipo);

                    if (anticipo.Estado == "PAGADO / PENDIENTE POR LEGALIZAR")
                    {
                        anticipo.SoporteNombre = soportePagoNombre;

                        var correoLegalizador = await _informixService.ObtenerCorreoPorRolAsync("Legalizador");

                        var destinatarios = new List<string>();

                        if (!string.IsNullOrWhiteSpace(anticipo.CorreoSolicitante))
                            destinatarios.Add(anticipo.CorreoSolicitante);

                        //if (!string.IsNullOrWhiteSpace(correoLegalizador))
                        //destinatarios.Add(correoLegalizador);

                            destinatarios.Add("powerapps@recamier.com");
                            /*destinatarios.Add("flmora@recamier.com");
                            destinatarios.Add("almesa@recamier.com");*/

                            destinatarios = destinatarios
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct()
                                .ToList();


                        await EnviarCorreoAsync(anticipo, _baseUrl, destinatarios);
                    }
                    else
                    {
                        _logger.LogInformation("No se envió correo porque el estado es '{Estado}'", anticipo.Estado);
                    }

                    return Ok(new { success = true, message = "Pago registrado correctamente.", soporte_pago = soportePagoNombre });
                }
                else
                {
                    _logger.LogWarning("No se encontró el anticipo {IdAnticipo} o no se pudo actualizar.", dto.IdAnticipo);
                    return NotFound(new { success = false, message = "No se encontró el anticipo o no se pudo actualizar." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al registrar el pago para el anticipo {IdAnticipo}", dto?.IdAnticipo);
                return StatusCode(500, new { success = false, message = "Error interno al registrar pago.", detalle = ex.Message });
            }
        }

        [HttpGet("consulta-legalizar-anticipos")]
        public async Task<IActionResult> ConsultaLegalizarAnticipos()
        {
            _logger.LogInformation("Iniciando consulta de anticipos por legalizar...");

            try
            {
                var anticipos = await _informixService.ConsultarLegalizarAnticiposAsync();

                if (anticipos == null || anticipos.Count == 0)
                {
                    _logger.LogWarning("No se encontraron anticipos por legalizar.");
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron anticipos por legalizar."
                    });
                }

                _logger.LogInformation($"Consulta exitosa. Registros encontrados: {anticipos.Count}");
                return Ok(new
                {
                    success = true,
                    total = anticipos.Count,
                    data = anticipos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar anticipos por legalizar.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al consultar anticipos por legalizar."
                });
            }
        }

        [HttpPost("registrar-legalizacion")]
        public async Task<IActionResult> RegistrarLegalizacion([FromBody] RegistrarLegalizacionDto dto)
        {
            try
            {
                if (dto == null || dto.IdAnticipo == 0)
                    return BadRequest(new { success = false, message = "Datos inválidos" });

                var actualizado = await _informixService.RegistrarLegalizacionAsync(dto.IdAnticipo, dto.Legalizado, dto.QuienLegaliza);

                if (actualizado)
                {
                    var anticipo = await _informixService.ConsultarSolicitudAnticipoPorIdAsync(dto.IdAnticipo);

                    if (anticipo.Estado == "FINALIZADO")
                    {
                        var correoLegalizador = await _informixService.ObtenerCorreoPorRolAsync("Legalizador");

                        var destinatarios = new List<string>();

                        if (!string.IsNullOrWhiteSpace(anticipo.CorreoSolicitante))
                            destinatarios.Add(anticipo.CorreoSolicitante);

                        //if (!string.IsNullOrWhiteSpace(anticipo.CorreoAprobador))
                            //destinatarios.Add(anticipo.CorreoAprobador);

                        //if (!string.IsNullOrWhiteSpace(correoLegalizador))
                        //destinatarios.Add(correoLegalizador);

                        destinatarios.Add("powerapps@recamier.com");
                        //destinatarios.Add("flmora@recamier.com");

                        destinatarios = destinatarios
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()
                            .ToList();

                        await EnviarCorreoAsync(anticipo, _baseUrl, destinatarios);

                        _logger.LogInformation("Correo de notificación enviado para estado 'FINALIZADO'.");
                    }

                    return Ok(new { success = true, message = "Legalización registrada exitosamente" });
                }
                else
                {
                    return StatusCode(500, new { success = false, message = "No se pudo registrar la legalización" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar la legalización.");
                return StatusCode(500, new { success = false, message = "Error interno al registrar la legalización." });
            }
        }

        [HttpGet("estadisticas/pagado")]
        public async Task<IActionResult> EstadisticaPagado([FromQuery] int? anio, [FromQuery] int? mes)
        {
            _logger.LogInformation("Consultando total pagado por mes…");
            try
            {
                var data = await _informixService.ObtenerTotalPagadoPorMesAsync(anio, mes);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar total pagado por mes.");
                return StatusCode(500, new { success = false, message = "Error interno al consultar total pagado por mes." });
            }
        }

        [HttpGet("estadisticas/anticipos-creados")]
        public async Task<IActionResult> EstadisticaAnticiposCreados([FromQuery] int? anio, [FromQuery] int? mes)
        {
            _logger.LogInformation("Consultando cantidad de anticipos creados por mes…");
            try
            {
                var data = await _informixService.ObtenerCantidadAnticiposPorMesAsync(anio, mes);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar anticipos creados por mes.");
                return StatusCode(500, new { success = false, message = "Error interno al consultar anticipos creados por mes." });
            }
        }

        [HttpGet("estadisticas/solicitado-vs-pagado")]
        public async Task<IActionResult> EstadisticaSolicitadoVsPagado([FromQuery] int? anio, [FromQuery] int? mes)
        {
            _logger.LogInformation("Consultando solicitado vs pagado por mes…");
            try
            {
                var data = await _informixService.ObtenerSolicitadoVsPagadoPorMesAsync(anio, mes);
                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar solicitado vs pagado.");
                return StatusCode(500, new { success = false, message = "Error interno al consultar solicitado vs pagado." });
            }
        }

        [HttpGet("aprobados-aprobador")]
        public async Task<IActionResult> ConsultarAnticiposAprobadosPorCorreo([FromQuery] string correoAprobador)
        {
            _logger.LogInformation("Consultando anticipos aprobados para el aprobador con correo: {Correo}", correoAprobador);

            try
            {
                var anticipos = await _informixService.ConsultarAnticiposAprobadosPorCorreoAsync(correoAprobador);

                if (anticipos == null || anticipos.Count == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontraron anticipos aprobados para este correo."
                    });
                }

                return Ok(new
                {
                    success = true,
                    total = anticipos.Count,
                    data = anticipos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar anticipos aprobados para el correo: {Correo}", correoAprobador);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno al consultar anticipos aprobados."
                });
            }
        }
        private async Task EnviarCorreoAsync(SolicitudAnticipo dto, string rootPath, List<string> destinatarios)
        {
            try
            {
                using var smtp = new SmtpClient(_smtp.Host, _smtp.Port)
                {
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_smtp.User, _smtp.Pass)
                };

                string numeroAnticipo = dto.IdAnticipo.ToString();

                foreach (var correo in destinatarios.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
                {
                    bool esSolicitante = !string.IsNullOrEmpty(dto.CorreoSolicitante) &&
                                         correo.Equals(dto.CorreoSolicitante, StringComparison.OrdinalIgnoreCase);

                    string cuerpoCorreo = GenerarCuerpoCorreo(dto, rootPath, esSolicitante);

                    using var mail = new MailMessage
                    {
                        From = new MailAddress(_smtp.User, "Notificaciones Anticipos"),
                        Subject = $"Actualización de Estado de Solicitud de Anticipo No.{numeroAnticipo}",
                        IsBodyHtml = true,
                        Body = cuerpoCorreo
                    };

                    mail.To.Add(correo);
                    AdjuntarSoporte(dto.SoporteNombre, mail);

                    await smtp.SendMailAsync(mail);
                    _logger.LogInformation("Correo enviado a: {Destino}", correo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar el correo de notificación.");
            }
        }
        private string GenerarCuerpoCorreo(SolicitudAnticipo dto, string rootPath, bool esSolicitante = false)
        {
            string numeroAnticipo = dto.IdAnticipo.ToString();
            string nombreDestinatario = dto.Solicitante;
            string anio = DateTime.Now.Year.ToString();

            string botonesAccion = "";
            if (!esSolicitante && dto.Estado == "PENDIENTE APROBACION")
            {
                botonesAccion = $@"
                <div style='text-align:center;margin-top:30px;'>
                  <a href='{rootPath}/?id={numeroAnticipo}&accion=aprobar'
                     style='display:inline-block;margin-right:10px;padding:12px 24px;
                            background-color:#28a745;color:#fff;
                            text-decoration:none;border-radius:4px;'>
                    Aprobar
                  </a>
                  <a href='{rootPath}/?id={numeroAnticipo}&accion=rechazar'
                     style='display:inline-block;margin-left:10px;padding:12px 24px;
                            background-color:#dc3545;color:#fff;
                            text-decoration:none;border-radius:4px;'>
                    Rechazar
                  </a>
                </div>";
            }

            string tablaInfoAnticipo = $@"
            <table style='width:100%;border-collapse:collapse;margin:22px 0;font-size:1em;'>
              <tr>
                <th style='padding:8px;border:1px solid #ccc;background:#f4f4f4;'>Campo</th>
                <th style='padding:8px;border:1px solid #ccc;background:#f4f4f4;'></th>
              </tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>ID Anticipo</td><td style='padding:8px;border:1px solid #ccc;'>{dto.IdAnticipo}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Nombre Proveedor</td><td style='padding:8px;border:1px solid #ccc;'>{dto.Proveedor ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Valor Anticipo</td><td style='padding:8px;border:1px solid #ccc;'>{dto.ValorAnticipo.ToString("C0")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Fecha Solicitud</td><td style='padding:8px;border:1px solid #ccc;'>{dto.FechaSolicitud.ToString("dd/MM/yyyy")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Estado</td><td style='padding:8px;border:1px solid #ccc;'>{dto.Estado ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Legalizado</td><td style='padding:8px;border:1px solid #ccc;'>{(dto.TieneLegalizacion == "1" || dto.TieneLegalizacion == "Sí" ? "Sí" : "No")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Solicitante</td><td style='padding:8px;border:1px solid #ccc;'>{dto.Solicitante ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Motivo Rechazo</td><td style='padding:8px;border:1px solid #ccc;'>{dto.MotivoRechazo ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>NIT Proveedor</td><td style='padding:8px;border:1px solid #ccc;'>{dto.NitProveedor ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>VP</td><td style='padding:8px;border:1px solid #ccc;'>{dto.VP ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Correo Aprobador</td><td style='padding:8px;border:1px solid #ccc;'>{dto.CorreoAprobador ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>¿Pagado?</td><td style='padding:8px;border:1px solid #ccc;'>{(dto.Pagado.HasValue && dto.Pagado.Value == 1 ? "Sí" : "No")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>¿Quién Legaliza?</td><td style='padding:8px;border:1px solid #ccc;'>{dto.QuienLegaliza ?? "-"}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Retención Fuente</td><td style='padding:8px;border:1px solid #ccc;'>{(dto.RetencionFuente.HasValue ? dto.RetencionFuente.Value.ToString("C0") : "0")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Retención ICA</td><td style='padding:8px;border:1px solid #ccc;'>{(dto.RetencionIca.HasValue ? dto.RetencionIca.Value.ToString("C0") : "0")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Retención IVA</td><td style='padding:8px;border:1px solid #ccc;'>{(dto.RetencionIva.HasValue ? dto.RetencionIva.Value.ToString("C0") : "0")}</td></tr>
              <tr><td style='padding:8px;border:1px solid #ccc;'>Valor a Pagar</td><td style='padding:8px;border:1px solid #ccc;'>{dto.ValorAPagar.ToString("C0")}</td></tr>
            </table>
            <div style='margin:12px 0 0 0;'>
              <strong>Concepto:</strong><br>
              <span style='background:#f8f8f8;padding:8px 12px;display:block;border-radius:4px;border:1px solid #e0e0e0;'>{dto.Concepto ?? "-"}</span>
            </div>
            ";

            return $@"
            <html>
              <body style='background-color:#f2f2f2;margin:0;padding:0;font-family:Arial,sans-serif;'>
                <div style='max-width:600px;margin:40px auto;border-radius:10px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.12);'>
                  <div style='background:#444444;padding:28px 0;text-align:center;color:#fff;font-size:1.4em;font-weight:bold;'>
                    Actualización de Estado de Solicitud de Anticipo No.{numeroAnticipo}
                  </div>
                  <div style='background:#fff;padding:32px 32px 24px 32px;font-size:1.08em;color:#222;'>
                    <p>Estimado/a <b>{nombreDestinatario}</b>,</p>
                    <p>Le informamos que el estado de su solicitud de anticipo ha cambiado.</p>
                    <p style='font-weight:bold;font-size:1.13em;margin-top:18px;'>
                      Nuevo Estado: <span style='color:#333;'>{dto.Estado}</span>
                    </p>
                    {tablaInfoAnticipo}
                    <p style='margin-top:30px;'>Para más detalles, por favor acceda a su cuenta en nuestro sistema.</p>
                    <p>Si tiene alguna pregunta, no dude en contactarnos.</p>
                    <p style='margin-top:30px;'>Saludos cordiales.</p>
                    {botonesAccion}
                  </div>
                  <div style='background:#444444;padding:8px 0;text-align:center;color:#fff;font-size:0.96em;'>
                    &copy; {anio} Recamier S.A. Todos los derechos reservados.
                  </div>
                </div>
              </body>
            </html>";
        }
        private void AdjuntarSoporte(string? soporteRelativo, MailMessage mail)
        {
            if (string.IsNullOrEmpty(soporteRelativo)) return;

            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), soporteRelativo);

            if (System.IO.File.Exists(fullPath))
            {
                mail.Attachments.Add(new Attachment(fullPath));
                _logger.LogInformation("Adjunto agregado: {Archivo}", fullPath);
            }
            else
            {
                _logger.LogWarning("Archivo no encontrado para adjunto: {Archivo}", fullPath);
            }
        }

    }
}
