namespace BackendAnticipos.Models
{
    public class RegistrarPagoFormDto
    {
        public int IdAnticipo { get; set; }
        public bool Pagado { get; set; }
        public List<IFormFile>? SoportesPago { get; set; }
    }
}
