using BackendAnticipos.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
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

        // LOGIN SOLO POR CORREO (ahora retorna lista de roles)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Correo) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Datos de login incompletos." });
            }

            var (isValid, roles, idUsuario, usuario) = await _authService.ValidarUsuarioAsync(request.Correo, request.Password);

            if (isValid)
            {
                return Ok(new
                {
                    success = true,
                    id_usuario = idUsuario,
                    usuario = usuario,
                    correo = request.Correo,
                    roles = roles // ahora es lista
                });
            }
            else
            {
                return Unauthorized(new { success = false, message = "Credenciales inválidas." });
            }
        }

        // REGISTRO: usuario, correo, password, roles (lista)
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Usuario) ||
                string.IsNullOrWhiteSpace(request.Correo) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                request.Roles == null || request.Roles.Count == 0)
            {
                return BadRequest(new { success = false, message = "Datos de registro incompletos." });
            }

            var registrado = await _authService.RegistrarUsuarioAsync(
                request.Usuario,
                request.Password,
                request.Roles,
                request.Correo
            );

            if (registrado)
            {
                return Ok(new { success = true, message = "Usuario registrado exitosamente." });
            }
            else
            {
                return Conflict(new { success = false, message = "El usuario o correo ya existe, o uno de los roles es inválido." });
            }
        }

        // DTOs internos del controlador (ajustados)
        public class LoginRequest
        {
            public string Correo { get; set; }
            public string Password { get; set; }
        }

        public class RegisterRequest
        {
            public string Usuario { get; set; }
            public string Correo { get; set; }
            public string Password { get; set; }
            public List<string> Roles { get; set; } // ahora es lista
        }
    }
}
