using BackendAnticipos.Models;
using BackendAnticipos.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;

namespace BackendAnticipos.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AprobadoresController : ControllerBase
    {
        private readonly InformixService _informixService;
        private readonly ILogger<AprobadoresController> _logger;

        public AprobadoresController(InformixService informixService, ILogger<AprobadoresController> logger)
        {
            _informixService = informixService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerAprobadores([FromQuery] bool? activo = null)
        {
            try
            {
                var aprobadores = await _informixService.ObtenerAprobadoresAsync(activo);
                var data = aprobadores.Select(a => new
                {
                    idAprobador = a.IdAprobador,
                    nombre = ConstruirNombreVisual(a.Cargo, a.Nombre),
                    correo = a.Correo,
                    activo = a.Activo
                }).ToList();

                return Ok(new
                {
                    success = true,
                    total = data.Count,
                    data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar aprobadores.");
                return StatusCode(500, new { success = false, message = "Error interno al consultar aprobadores." });
            }
        }

        [HttpGet("{idAprobador:int}")]
        public async Task<IActionResult> ObtenerAprobadorPorId(int idAprobador)
        {
            if (idAprobador <= 0)
                return BadRequest(new { success = false, message = "Id de aprobador invalido." });

            try
            {
                var aprobador = await _informixService.ObtenerAprobadorPorIdAsync(idAprobador);
                if (aprobador == null)
                    return NotFound(new { success = false, message = "Aprobador no encontrado." });

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        idAprobador = aprobador.IdAprobador,
                        nombre = ConstruirNombreVisual(aprobador.Cargo, aprobador.Nombre),
                        correo = aprobador.Correo,
                        activo = aprobador.Activo
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar aprobador {IdAprobador}.", idAprobador);
                return StatusCode(500, new { success = false, message = "Error interno al consultar aprobador." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CrearAprobador([FromBody] CrearAprobadorRequest request)
        {
            if (!EsRequestValido(request?.Cargo, request?.Nombre, request?.Correo, out var mensaje))
                return BadRequest(new { success = false, message = mensaje });

            try
            {
                var idAprobador = await _informixService.CrearAprobadorAsync(request!.Cargo, request.Nombre, request.Correo);

                return Ok(new
                {
                    success = true,
                    message = "Aprobador creado correctamente.",
                    idAprobador
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear aprobador con correo {Correo}.", request?.Correo);
                return StatusCode(500, new { success = false, message = "Error interno al crear aprobador." });
            }
        }

        [HttpPut("{idAprobador:int}")]
        public async Task<IActionResult> ActualizarAprobador(int idAprobador, [FromBody] ActualizarAprobadorRequest request)
        {
            if (idAprobador <= 0)
                return BadRequest(new { success = false, message = "Id de aprobador invalido." });

            if (!EsRequestValido(request?.Cargo, request?.Nombre, request?.Correo, out var mensaje))
                return BadRequest(new { success = false, message = mensaje });

            try
            {
                var actualizado = await _informixService.ActualizarAprobadorAsync(
                    idAprobador,
                    request!.Cargo,
                    request!.Nombre,
                    request.Correo,
                    request.Activo
                );

                if (!actualizado)
                    return NotFound(new { success = false, message = "Aprobador no encontrado." });

                return Ok(new { success = true, message = "Aprobador actualizado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar aprobador {IdAprobador}.", idAprobador);
                return StatusCode(500, new { success = false, message = "Error interno al actualizar aprobador." });
            }
        }

        [HttpDelete("{idAprobador:int}")]
        public async Task<IActionResult> EliminarAprobador(int idAprobador)
        {
            if (idAprobador <= 0)
                return BadRequest(new { success = false, message = "Id de aprobador invalido." });

            try
            {
                var eliminado = await _informixService.EliminarAprobadorAsync(idAprobador);

                if (!eliminado)
                    return NotFound(new { success = false, message = "Aprobador no encontrado." });

                return Ok(new { success = true, message = "Aprobador eliminado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar aprobador {IdAprobador}.", idAprobador);
                return StatusCode(500, new { success = false, message = "Error interno al eliminar aprobador." });
            }
        }

        private static bool EsRequestValido(string? cargo, string? nombre, string? correo, out string message)
        {
            if (string.IsNullOrWhiteSpace(cargo))
            {
                message = "El cargo del aprobador es obligatorio.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                message = "El nombre del aprobador es obligatorio.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(correo))
            {
                message = "El correo del aprobador es obligatorio.";
                return false;
            }

            try
            {
                _ = new MailAddress(correo.Trim());
            }
            catch
            {
                message = "Correo de aprobador invalido.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string ConstruirNombreVisual(string? cargo, string? nombrePersona)
        {
            var c = (cargo ?? string.Empty).Trim();
            var n = (nombrePersona ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(c)) return n;
            if (string.IsNullOrEmpty(n)) return c;
            return $"{c} - {n}";
        }
    }
}

