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
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Datos de login incompletos." });
            }

            var (isValid, rol, idUsuario) = await _authService.ValidarUsuarioAsync(request.Username, request.Password);

            if (isValid)
            {
                return Ok(new
                {
                    success = true,
                    id_usuario = idUsuario,
                    username = request.Username,
                    rol = rol
                });
            }
            else
            {
                return Unauthorized(new { success = false, message = "Credenciales inválidas." });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Rol))
            {
                return BadRequest(new { success = false, message = "Datos de registro incompletos." });
            }

            var registrado = await _authService.RegistrarUsuarioAsync(request.Username, request.Password, request.Rol);

            if (registrado)
            {
                return Ok(new { success = true, message = "Usuario registrado exitosamente." });
            }
            else
            {
                return Conflict(new { success = false, message = "El usuario ya existe o el rol es inválido." });
            }
        }

        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class RegisterRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string Rol { get; set; }
        }
    }
}
