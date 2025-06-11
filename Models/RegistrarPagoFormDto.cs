namespace BackendAnticipos.Models
{
    public class RegistrarPagoFormDto
    {
        public int IdAnticipo { get; set; }
        public bool Pagado { get; set; }
        public IFormFile? SoportePago { get; set; }
    }
}
