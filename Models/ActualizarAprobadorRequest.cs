namespace BackendAnticipos.Models
{
    public class ActualizarAprobadorRequest
    {
        public string Cargo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
    }
}
