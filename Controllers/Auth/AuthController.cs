using BackendAnticipos.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace BackendAnticipos.Controllers.Auth
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (Request?.Host.HasValue == true &&
                Request.Host.Host.Equals("192.168.20.30", System.StringComparison.OrdinalIgnoreCase) &&
                Request.Host.Port == 8089)
            {
                return StatusCode(403, new
                {
                    success = false,
                    message = "El dominio utilizado para iniciar sesión no está permitido. Por favor ingrese por el dominio oficial."
                });
            }

            if (request == null)
                return BadRequest(new { success = false, message = "Datos incompletos." });

            if (!string.IsNullOrWhiteSpace(request.Code))
            {
                var result = await _authService.ValidarUsuarioPorSsoCodeAsync(request.Code);

                if (result.IsValid)
                {
                    return Ok(new
                    {
                        success = true,
                        id_usuario = result.IdUsuario,
                        usuario = result.Usuario,
                        correo = result.Correo,
                        roles = result.Roles
                    });
                }

                return Unauthorized(new { success = false, message = "Código SSO inválido o expirado." });
            }

            if (string.IsNullOrWhiteSpace(request.Correo) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { success = false, message = "Datos incompletos." });

            var login = await _authService.ValidarUsuarioAsync(request.Correo, request.Password);

            if (login.IsValid)
            {
                return Ok(new
                {
                    success = true,
                    id_usuario = login.IdUsuario,
                    usuario = login.Usuario,
                    correo = login.Correo,
                    roles = login.Roles
                });
            }

            return Unauthorized(new { success = false, message = "Credenciales inválidas." });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request == null)
                return BadRequest(new { success = false, message = "Datos incompletos." });

            var password = request.Password?.Trim();
            var confirmPassword =
                request.ConfirmPassword?.Trim()
                ?? request.RepeatPassword?.Trim()
                ?? request.ConfirmarContrasena?.Trim()
                ?? request.RepetirContrasena?.Trim();

            if (!string.IsNullOrWhiteSpace(confirmPassword) &&
                !string.Equals(password, confirmPassword, System.StringComparison.Ordinal))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La contraseña y su confirmación no coinciden."
                });
            }

            var result = await _authService.RegistrarUsuarioAsync(
                request.Usuario,
                request.Correo,
                password,
                request.Area
            );

            if (!result.IsRegistered)
            {
                var statusCode = result.Message.Contains("Ya existe", System.StringComparison.OrdinalIgnoreCase) ? 409 : 400;
                return StatusCode(statusCode, new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                id_usuario = result.IdUsuario,
                usuario = result.Usuario,
                correo = result.Correo,
                roles = result.Roles
            });
        }

        public class LoginRequest
        {
            public string? Correo { get; set; }
            public string? Password { get; set; }
            public string? Code { get; set; }
        }

        public class RegisterRequest
        {
            public string? Usuario { get; set; }
            public string? Correo { get; set; }
            public string? Area { get; set; }
            public string? Password { get; set; }
            public string? ConfirmPassword { get; set; }
            public string? RepeatPassword { get; set; }
            public string? ConfirmarContrasena { get; set; }
            public string? RepetirContrasena { get; set; }
        }
    }
}
