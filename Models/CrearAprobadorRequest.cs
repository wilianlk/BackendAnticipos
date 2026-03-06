namespace BackendAnticipos.Models
{
    public class CrearAprobadorRequest
    {
        public string Cargo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
    }
}
