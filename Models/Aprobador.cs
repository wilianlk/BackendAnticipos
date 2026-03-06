namespace BackendAnticipos.Models
{
    public class Aprobador
    {
        public int IdAprobador { get; set; }
        public string Cargo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
    }
}
